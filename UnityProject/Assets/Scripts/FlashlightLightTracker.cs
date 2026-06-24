using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public sealed class FlashlightLightTracker : MonoBehaviour
    {
        private const string ProcessLabel = "FlashlightTracking";

        [Header("Flashlight Detection")]
        [SerializeField, Min(0)] private int cameraDeviceIndex;
        [SerializeField, Range(0, 255)] private int brightnessThreshold = 245;
        [SerializeField, Range(0, 255)] private int maxSaturation = 120;
        [SerializeField, Min(0.0f)] private float minBlobArea = 120.0f;
        [SerializeField] private bool mirrorViewportX;

        [HideInInspector, SerializeField] private bool launchOnStart = true;
        [HideInInspector, SerializeField] private bool stopProcessOnDisable = true;
        [HideInInspector, SerializeField] private string pythonExecutablePath = @"D:\anaconda3\envs\artifact\python.exe";
        [HideInInspector, SerializeField] private string workingDirectory = @"D:\Unity-HandTracking";
        [HideInInspector, SerializeField] private string scriptName = @"python\FlashlightTracking.py";
        [HideInInspector, SerializeField] private int udpPort = 5056;
        [HideInInspector, SerializeField] private int cameraWidth = 1280;
        [HideInInspector, SerializeField] private int cameraHeight = 720;
        [HideInInspector, SerializeField] private int cameraFps = 60;
        [HideInInspector, SerializeField] private float maxBlobAreaRatio = 0.2f;
        [HideInInspector, SerializeField] private bool showPreview = true;
        [HideInInspector, SerializeField] private bool mirrorViewportY;
        [HideInInspector, SerializeField] private Transform lightTransform;
        [HideInInspector, SerializeField] private Camera targetCamera;
        [HideInInspector, SerializeField] private float followPlaneZ = 0.05f;
        [HideInInspector, SerializeField] private Vector3 worldOffset;
        [SerializeField, Min(0.0f)] private float followSmoothing = 60.0f;
        [HideInInspector, SerializeField] private bool startHiddenUntilFirstLight = true;
        [HideInInspector, SerializeField] private bool hideWhenLightLost;
        [HideInInspector, SerializeField] private float lightLostTimeoutSeconds = 0.35f;

        private readonly object packetLock = new object();
        private Process launchedProcess;
        private Thread receiveThread;
        private UdpClient client;
        private Renderer[] lightRenderers;
        private volatile bool isReceiving;
        private string latestPacket;
        private DateTime latestPacketUtc = DateTime.MinValue;
        private bool lightWasVisible;
        private bool hasReceivedLight;
        private string pendingError;

        private void Reset()
        {
            lightTransform = transform;
        }

        private void Awake()
        {
            CacheLightRenderers();
            SetLightVisible(!startHiddenUntilFirstLight && !hideWhenLightLost);
        }

        private void Start()
        {
            StartReceiver();

            if (launchOnStart)
            {
                Launch();
            }
        }

        private void Update()
        {
            FlushPendingError();
            ApplyLatestPacket();
        }

        private void OnDisable()
        {
            StopReceiver();

            if (stopProcessOnDisable)
            {
                StopProcess();
            }
        }

        private void OnDestroy()
        {
            StopReceiver();

            if (stopProcessOnDisable)
            {
                StopProcess();
            }
        }

        public void Launch()
        {
            if (launchedProcess != null)
            {
                if (!launchedProcess.HasExited)
                {
                    return;
                }

                launchedProcess.Dispose();
                launchedProcess = null;
            }

            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                Debug.LogWarning($"{ProcessLabel}: working directory was not found: {workingDirectory}");
                return;
            }

            if (string.IsNullOrWhiteSpace(pythonExecutablePath) || !File.Exists(pythonExecutablePath))
            {
                Debug.LogWarning($"{ProcessLabel}: python executable was not found: {pythonExecutablePath}");
                return;
            }

            string scriptPath = Path.Combine(workingDirectory, scriptName);
            if (string.IsNullOrWhiteSpace(scriptName) || !File.Exists(scriptPath))
            {
                Debug.LogWarning($"{ProcessLabel}: script was not found: {scriptPath}");
                return;
            }

            string previewArgument = showPreview ? " --show" : string.Empty;
            string command =
                $"$Host.UI.RawUI.WindowTitle = {QuotePowerShellArgument(ProcessLabel)}; " +
                $"Set-Location -LiteralPath {QuotePowerShellArgument(workingDirectory)}; " +
                $"& {QuotePowerShellArgument(pythonExecutablePath)} {QuotePowerShellArgument(scriptPath)} " +
                $"--camera {cameraDeviceIndex} " +
                $"--udp-port {udpPort} " +
                $"--width {cameraWidth} " +
                $"--height {cameraHeight} " +
                $"--fps {cameraFps} " +
                $"--threshold {brightnessThreshold} " +
                $"--max-saturation {maxSaturation} " +
                $"--min-area {minBlobArea.ToString(CultureInfo.InvariantCulture)} " +
                $"--max-area-ratio {maxBlobAreaRatio.ToString(CultureInfo.InvariantCulture)}" +
                previewArgument;

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -Command {EscapeWindowsArgument(command)}",
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            launchedProcess = new Process { StartInfo = startInfo };
            try
            {
                launchedProcess.Start();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{ProcessLabel}: terminal launch failed: {exception.Message}");
                launchedProcess.Dispose();
                launchedProcess = null;
            }
        }

        private void StartReceiver()
        {
            if (isReceiving)
            {
                return;
            }

            try
            {
                client = new UdpClient(udpPort);
            }
            catch (SocketException exception)
            {
                pendingError = $"{ProcessLabel}: failed to bind UDP port {udpPort}: {exception.Message}";
                isReceiving = false;
                return;
            }

            isReceiving = true;
            receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true
            };
            receiveThread.Start();
        }

        private void StopReceiver()
        {
            isReceiving = false;
            client?.Close();
            client = null;

            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(200);
            }

            receiveThread = null;
        }

        private void ReceiveLoop()
        {
            while (isReceiving)
            {
                try
                {
                    IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] dataBytes = client.Receive(ref anyIP);
                    string packet = Encoding.UTF8.GetString(dataBytes);

                    lock (packetLock)
                    {
                        latestPacket = packet;
                        latestPacketUtc = DateTime.UtcNow;
                    }
                }
                catch (SocketException)
                {
                    if (isReceiving)
                    {
                        pendingError = $"{ProcessLabel}: lost UDP connection on port {udpPort}.";
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private void ApplyLatestPacket()
        {
            string packet;
            DateTime packetUtc;
            lock (packetLock)
            {
                packet = latestPacket;
                packetUtc = latestPacketUtc;
                latestPacket = null;
            }

            if (!string.IsNullOrWhiteSpace(packet) &&
                TryParseLightPacket(packet, out Vector2 viewportPosition))
            {
                viewportPosition = ApplyViewportMirroring(viewportPosition);
                ApplyViewportPosition(viewportPosition);
                lightWasVisible = true;
                return;
            }

            if (lightWasVisible && (DateTime.UtcNow - packetUtc).TotalSeconds > lightLostTimeoutSeconds)
            {
                lightWasVisible = false;

                if (hideWhenLightLost)
                {
                    SetLightVisible(false);
                }
            }
        }

        private void ApplyViewportPosition(Vector2 viewportPosition)
        {
            if (lightTransform == null || targetCamera == null)
            {
                return;
            }

            Ray ray = targetCamera.ViewportPointToRay(new Vector3(viewportPosition.x, viewportPosition.y, 0.0f));
            Plane followPlane = new Plane(Vector3.forward, new Vector3(0.0f, 0.0f, followPlaneZ));
            if (!followPlane.Raycast(ray, out float distance))
            {
                return;
            }

            Vector3 targetPosition = ray.GetPoint(distance) + worldOffset;
            if (!hasReceivedLight || !lightWasVisible)
            {
                lightTransform.position = targetPosition;
                hasReceivedLight = true;
            }
            else
            {
                float blend = GetFrameBlend(followSmoothing);
                lightTransform.position = Vector3.Lerp(lightTransform.position, targetPosition, blend);
            }

            SetLightVisible(true);
        }

        private static bool TryParseLightPacket(string packet, out Vector2 viewportPosition)
        {
            viewportPosition = default;
            string[] values = packet.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < 4 || !string.Equals(values[0], "FLASHLIGHT", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                IsViewportValue(x) &&
                IsViewportValue(y) &&
                SetViewport(out viewportPosition, x, y);
        }

        private static bool IsViewportValue(float value)
        {
            return value >= -0.01f && value <= 1.01f;
        }

        private static bool SetViewport(out Vector2 viewportPosition, float x, float y)
        {
            viewportPosition = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
            return true;
        }

        private Vector2 ApplyViewportMirroring(Vector2 viewportPosition)
        {
            if (mirrorViewportX)
            {
                viewportPosition.x = 1.0f - viewportPosition.x;
            }

            if (mirrorViewportY)
            {
                viewportPosition.y = 1.0f - viewportPosition.y;
            }

            return viewportPosition;
        }

        private void CacheLightRenderers()
        {
            if (lightTransform == null)
            {
                lightRenderers = Array.Empty<Renderer>();
                return;
            }

            lightRenderers = lightTransform.GetComponentsInChildren<Renderer>(true);
        }

        private void SetLightVisible(bool visible)
        {
            if (lightRenderers == null)
            {
                return;
            }

            for (int i = 0; i < lightRenderers.Length; i++)
            {
                if (lightRenderers[i] != null)
                {
                    lightRenderers[i].enabled = visible;
                }
            }
        }

        private void FlushPendingError()
        {
            if (string.IsNullOrEmpty(pendingError))
            {
                return;
            }

            Debug.LogWarning(pendingError);
            pendingError = null;
        }

        private void StopProcess()
        {
            if (launchedProcess == null)
            {
                return;
            }

            try
            {
                if (!launchedProcess.HasExited)
                {
                    KillProcessTree(launchedProcess.Id);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                Debug.LogWarning($"{ProcessLabel}: process cleanup failed: {exception.Message}");
            }
            finally
            {
                launchedProcess.Dispose();
                launchedProcess = null;
            }
        }

        private static void KillProcessTree(int processId)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/PID {processId} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process taskkill = Process.Start(startInfo);
            taskkill?.WaitForExit(2000);
        }

        private static float GetFrameBlend(float speed)
        {
            if (speed <= 0.0f)
            {
                return 1.0f;
            }

            return 1.0f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        }

        private static string EscapeWindowsArgument(string value)
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static string QuotePowerShellArgument(string value)
        {
            return $"'{value.Replace("'", "''")}'";
        }
    }
}
