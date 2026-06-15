using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public sealed class Mission4ArucoCameraSystem : MonoBehaviour
    {
        private const string ProcessLabel = "ArucoTracking";

        [Header("Process")]
        [SerializeField] private bool launchOnStart = true;
        [SerializeField] private bool stopProcessOnDisable = true;
        [SerializeField] private string pythonExecutablePath = @"C:\Users\creal\miniconda3\envs\artifact\python.exe";
        [SerializeField] private string workingDirectory = @"C:\capstone\Shadow-to-3D-Generator";
        [SerializeField] private string scriptName = @"python\ArucoTracking.py";

        [Header("Marker")]
        [SerializeField, Min(0)] private int cameraDeviceIndex;
        [SerializeField] private string dictionaryName = "DICT_4X4_50";
        [SerializeField] private int markerId;
        [SerializeField, Min(1)] private int udpPort = 5054;
        [SerializeField] private bool mirrorViewportX = true;
        [SerializeField] private bool mirrorViewportY;

        [Header("References")]
        [SerializeField] private ArucoMarkerFollower markerFollower;

        private readonly object packetLock = new object();
        private Process launchedProcess;
        private Thread receiveThread;
        private UdpClient client;
        private volatile bool isReceiving;
        private string latestPacket;
        private DateTime latestPacketUtc = DateTime.MinValue;
        private bool markerWasVisible;
        private string pendingError;

        private void Start()
        {
            ResolveReferences();
            StartReceiver();

            if (launchOnStart)
            {
                Launch();
            }
        }

        public void BeginTracking()
        {
            ResolveReferences();
            StartReceiver();
            Launch();
        }

        public void StopTracking()
        {
            StopReceiver();

            if (stopProcessOnDisable)
            {
                StopProcess();
            }
        }

        private void Update()
        {
            ResolveReferences();
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

            string command =
                $"$Host.UI.RawUI.WindowTitle = {QuotePowerShellArgument(ProcessLabel)}; " +
                $"Set-Location -LiteralPath {QuotePowerShellArgument(workingDirectory)}; " +
                $"& {QuotePowerShellArgument(pythonExecutablePath)} {QuotePowerShellArgument(scriptPath)} " +
                $"--camera {cameraDeviceIndex} " +
                $"--dictionary {dictionaryName} " +
                $"--marker-id {markerId} " +
                $"--udp-port {udpPort}";

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
                    string packet = System.Text.Encoding.UTF8.GetString(dataBytes);

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
            if (markerFollower == null)
            {
                return;
            }

            string packet;
            DateTime packetUtc;
            lock (packetLock)
            {
                packet = latestPacket;
                packetUtc = latestPacketUtc;
                latestPacket = null;
            }

            if (!string.IsNullOrWhiteSpace(packet) &&
                TryParseMarkerPacket(packet, out string dictionary, out int id, out Vector2 viewportPosition, out float rotationDegrees))
            {
                viewportPosition = ApplyViewportMirroring(viewportPosition);
                markerFollower.SetMarkerViewportPose(dictionary, id, viewportPosition, rotationDegrees);
                markerWasVisible = true;
                return;
            }

            if (markerWasVisible && (DateTime.UtcNow - packetUtc).TotalSeconds > 0.35f)
            {
                markerFollower.MarkMarkerLost(dictionaryName, markerId);
                markerWasVisible = false;
            }
        }

        private static bool TryParseMarkerPacket(
            string packet,
            out string dictionary,
            out int id,
            out Vector2 viewportPosition,
            out float rotationDegrees)
        {
            dictionary = string.Empty;
            id = -1;
            viewportPosition = default;
            rotationDegrees = 0.0f;

            string[] values = packet.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < 5)
            {
                return false;
            }

            dictionary = values[0];
            return int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) &&
                float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(values[4], NumberStyles.Float, CultureInfo.InvariantCulture, out rotationDegrees) &&
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

        private void ResolveReferences()
        {
            if (markerFollower == null)
            {
                markerFollower = FindObjectOfType<ArucoMarkerFollower>();
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
