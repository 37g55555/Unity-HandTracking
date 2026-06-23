using System.Collections;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public sealed class Mission5SeesawShadowSystem : MonoBehaviour
    {
        private const string ProcessLabel = "Shadow Area";

        [Header("Camera Process")]
        [SerializeField] private bool launchOnStart = true;
        [SerializeField] private bool stopProcessOnDisable = true;
        [SerializeField] private string pythonExecutablePath = @"C:\Users\creal\miniconda3\envs\artifact\python.exe";
        [SerializeField] private string workingDirectory = @"C:\capstone\Shadow-to-3D-Generator";
        [SerializeField] private string scriptName = @"python\Mission5ShadowAreaTracking.py";
        [SerializeField, Min(0)] private int cameraDeviceIndex;
        [SerializeField, Min(1)] private int udpPort = 5055;
        [SerializeField, Min(0.0f)] private float staleDataTimeoutSeconds = 1.0f;

        [Header("Weight Mapping")]
        [SerializeField, Range(0.0f, 1.0f)] private float minimumAddedShadowRatio = 0.015f;
        [SerializeField, Range(0.0f, 1.0f)] private float maximumAddedShadowRatio = 0.50f;
        [SerializeField, Min(0.0f)] private float shadowWeightSmoothing = 6.0f;
        [SerializeField, Min(0.0f)] private float rawRatioSmoothing = 8.0f;

        [Header("Seesaw")]
        [SerializeField] private Transform seesawPivot;
        [SerializeField] private Transform seesawBeam;
        [SerializeField] private Transform leftSeat;
        [SerializeField] private Transform rightSeat;
        [SerializeField] private Transform leftSeatAnchor;
        [SerializeField] private Transform rightSeatAnchor;
        [SerializeField] private Transform shadowStar;
        [SerializeField] private Transform fulcrum;
        [SerializeField] private Vector3 beamBaseEulerAngles = new Vector3(-110.0f, 90.0f, -90.0f);
        [SerializeField] private Vector3 pivotPosition = new Vector3(0.0f, -3.17f, 0.0f);
        [SerializeField] private Vector3 seatWorldEulerAngles = new Vector3(-90.0f, 0.0f, 0.0f);
        [SerializeField] private Vector3 shadowStarSeatOffset = new Vector3(0.0f, 0.82f, 0.0f);
        [SerializeField] private float completedBeamEulerX = -70.0f;

        [Header("Completion")]
        [SerializeField, Range(0.0f, 1.0f)] private float completionShadowAreaRatio = 0.50f;
        [SerializeField] private string nextSceneName = "Ending";
        [SerializeField, Min(0.0f)] private float endingTransitionDelay = 0.6f;
        [SerializeField, Min(0.0f)] private float postOutroSceneTransitionDelay = 1.0f;
        [SerializeField, Range(0.0f, 1.0f)] private float completionDingVolume = 0.9f;

        private readonly object packetLock = new object();
        private Process launchedProcess;
        private Thread receiveThread;
        private UdpClient client;
        private volatile bool isReceiving;
        private DateTime latestPacketUtc = DateTime.MinValue;
        private float latestShadowRatio;
        private bool hasLatestShadowRatio;
        private bool hasSmoothedRatio;
        private float smoothedRawRatio;
        private float smoothedWeight;
        private bool hasInitialBeamPose;
        private Vector3 initialBeamLocalPosition;
        private bool hasInitialFulcrumPose;
        private Vector3 initialFulcrumWorldPosition;
        private Quaternion initialFulcrumWorldRotation;
        private AudioClip completionDingClip;
        private bool completionTriggered;
        private bool releaseShadowStarForOutro;
        private bool missingReferencesWarned;
        private string pendingError;

        private void Start()
        {
            ResolveReferences();
            CacheInitialBeamPose();
            CacheInitialFulcrumPose();
            DisableMission4ShadowStarBehaviours();
            StartReceiver();

            if (launchOnStart)
            {
                Launch();
            }
        }

        private void LateUpdate()
        {
            ResolveReferences();
            CacheInitialBeamPose();
            CacheInitialFulcrumPose();
            DisableMission4ShadowStarBehaviours();
            FlushPendingError();
            UpdateSeesawWeightFromCameraRatio();
            UpdateSeesawPose();
        }

        private void OnValidate()
        {
            maximumAddedShadowRatio = Mathf.Max(maximumAddedShadowRatio, minimumAddedShadowRatio + 0.001f);
            completionShadowAreaRatio = Mathf.Clamp01(completionShadowAreaRatio);
            staleDataTimeoutSeconds = Mathf.Max(0.0f, staleDataTimeoutSeconds);
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
                "--width 640 --height 360 --fps 30 --camera-buffer-size 1 " +
                "--camera-auto-exposure 0.75 --preview " +
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
                StartCoroutine(TerminalWindowRouter.MoveToConfiguredDisplayRoutine(launchedProcess, ProcessLabel));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{ProcessLabel}: terminal launch failed: {exception.Message}");
                launchedProcess.Dispose();
                launchedProcess = null;
            }
        }

        public void DebugTriggerCompletion()
        {
            if (!completionTriggered)
            {
                TriggerCompletion();
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

                    if (TryParseShadowRatioPacket(packet, out float ratio))
                    {
                        lock (packetLock)
                        {
                            latestShadowRatio = Clamp01(ratio);
                            latestPacketUtc = DateTime.UtcNow;
                            hasLatestShadowRatio = true;
                        }
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

        private void UpdateSeesawWeightFromCameraRatio()
        {
            if (completionTriggered)
            {
                return;
            }

            if (!TryGetLatestShadowRatio(out float rawRatio))
            {
                SmoothWeightToward(0.0f);
                return;
            }

            if (!hasSmoothedRatio)
            {
                smoothedRawRatio = rawRatio;
                hasSmoothedRatio = true;
            }
            else
            {
                smoothedRawRatio = Mathf.Lerp(smoothedRawRatio, rawRatio, GetFrameBlend(rawRatioSmoothing));
            }

            float normalizedWeight = Mathf.InverseLerp(
                minimumAddedShadowRatio,
                maximumAddedShadowRatio,
                smoothedRawRatio);
            float easedWeight = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(normalizedWeight));
            SmoothWeightToward(easedWeight);

            if (rawRatio >= completionShadowAreaRatio)
            {
                TriggerCompletion();
            }
        }

        private bool TryGetLatestShadowRatio(out float ratio)
        {
            lock (packetLock)
            {
                ratio = latestShadowRatio;
                if (!hasLatestShadowRatio)
                {
                    return false;
                }

                if (staleDataTimeoutSeconds > 0.0f &&
                    (DateTime.UtcNow - latestPacketUtc).TotalSeconds > staleDataTimeoutSeconds)
                {
                    return false;
                }

                return true;
            }
        }

        private void SmoothWeightToward(float targetWeight)
        {
            smoothedWeight = Mathf.Lerp(smoothedWeight, Mathf.Clamp01(targetWeight), GetFrameBlend(shadowWeightSmoothing));
        }

        private void UpdateSeesawPose()
        {
            if (seesawPivot == null)
            {
                return;
            }

            seesawPivot.position = pivotPosition;
            float tiltAmount = completionTriggered ? 1.0f : smoothedWeight;
            seesawPivot.rotation = Quaternion.identity;

            if (seesawBeam != null)
            {
                CacheInitialBeamPose();
                seesawBeam.localPosition = initialBeamLocalPosition;
                float beamEulerX = Mathf.LerpAngle(beamBaseEulerAngles.x, completedBeamEulerX, tiltAmount);
                seesawBeam.localRotation = Quaternion.Euler(
                    beamEulerX,
                    beamBaseEulerAngles.y,
                    beamBaseEulerAngles.z);
            }

            RestoreFulcrumPose();

            Vector3 leftPoint = GetSeatPoint(true);
            Vector3 rightPoint = GetSeatPoint(false);

            UpdateSeatPose(leftSeat, leftPoint);
            UpdateSeatPose(rightSeat, rightPoint);

            if (shadowStar != null && !releaseShadowStarForOutro)
            {
                shadowStar.position = leftPoint + shadowStarSeatOffset;
                shadowStar.rotation = Quaternion.identity;
            }
        }

        private void ResolveReferences()
        {
            if (!missingReferencesWarned &&
                (seesawPivot == null ||
                 seesawBeam == null ||
                 leftSeat == null ||
                 rightSeat == null ||
                 leftSeatAnchor == null ||
                 rightSeatAnchor == null ||
                 shadowStar == null ||
                 fulcrum == null))
            {
                missingReferencesWarned = true;
                Debug.LogWarning("Mission5SeesawShadowSystem: assign all Mission5 scene references in the inspector.");
            }
        }

        private void CacheInitialBeamPose()
        {
            if (hasInitialBeamPose || seesawBeam == null)
            {
                return;
            }

            initialBeamLocalPosition = seesawBeam.localPosition;
            hasInitialBeamPose = true;
        }

        private void CacheInitialFulcrumPose()
        {
            if (hasInitialFulcrumPose || fulcrum == null)
            {
                return;
            }

            initialFulcrumWorldPosition = fulcrum.position;
            initialFulcrumWorldRotation = fulcrum.rotation;
            hasInitialFulcrumPose = true;
        }

        private void RestoreFulcrumPose()
        {
            if (!hasInitialFulcrumPose || fulcrum == null)
            {
                return;
            }

            fulcrum.position = initialFulcrumWorldPosition;
            fulcrum.rotation = initialFulcrumWorldRotation;
        }

        private Vector3 GetSeatPoint(bool isLeftSeat)
        {
            Transform anchor = isLeftSeat ? leftSeatAnchor : rightSeatAnchor;
            if (anchor != null)
            {
                return anchor.position;
            }

            return isLeftSeat && leftSeat != null
                ? leftSeat.position
                : rightSeat != null
                    ? rightSeat.position
                    : seesawPivot.position;
        }

        private void UpdateSeatPose(Transform seat, Vector3 position)
        {
            if (seat == null)
            {
                return;
            }

            seat.position = position;
            seat.rotation = Quaternion.Euler(seatWorldEulerAngles);
        }

        private void DisableMission4ShadowStarBehaviours()
        {
            if (shadowStar == null)
            {
                return;
            }

            Mission4DoorTransition[] doorTransitions = shadowStar.GetComponents<Mission4DoorTransition>();
            for (int i = 0; i < doorTransitions.Length; i++)
            {
                doorTransitions[i].enabled = false;
            }

            Mission4ShadowStarLightFollower[] lightFollowers = shadowStar.GetComponents<Mission4ShadowStarLightFollower>();
            for (int i = 0; i < lightFollowers.Length; i++)
            {
                lightFollowers[i].enabled = false;
            }
        }

        private void TriggerCompletion()
        {
            completionTriggered = true;
            smoothedRawRatio = Mathf.Max(smoothedRawRatio, completionShadowAreaRatio);
            smoothedWeight = 1.0f;
            UpdateSeesawPose();
            PlayCompletionDing();
            FindObjectOfType<Mission5Controller>()?.HideInteractionInstruction();
            StartCoroutine(LoadEndingAfterDelay());
        }

        private IEnumerator LoadEndingAfterDelay()
        {
            if (endingTransitionDelay > 0.0f)
            {
                yield return new WaitForSeconds(endingTransitionDelay);
            }

            releaseShadowStarForOutro = true;
            Mission5Controller mission5Controller = FindObjectOfType<Mission5Controller>();
            if (mission5Controller != null)
            {
                yield return mission5Controller.PlayOutroRoutine();
            }

            if (postOutroSceneTransitionDelay > 0.0f)
            {
                yield return new WaitForSeconds(postOutroSceneTransitionDelay);
            }

            FindObjectOfType<GameStateManager>()?.OnEndingStarted();

            SceneFlowController sceneFlowController = FindObjectOfType<SceneFlowController>();
            if (sceneFlowController != null)
            {
                sceneFlowController.LoadScene(nextSceneName);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
            }
        }

        private void PlayCompletionDing()
        {
            if (completionDingVolume <= 0.0f)
            {
                return;
            }

            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.0f;

            if (completionDingClip == null)
            {
                completionDingClip = CreateCompletionDingClip();
            }

            audioSource.PlayOneShot(completionDingClip, completionDingVolume);
        }

        private static AudioClip CreateCompletionDingClip()
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.58f;
            int sampleCount = Mathf.CeilToInt(sampleRate * durationSeconds);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float attack = Mathf.Clamp01(time / 0.018f);
                float decay = Mathf.Exp(-5.2f * time);
                float primary = Mathf.Sin(2.0f * Mathf.PI * 880.0f * time);
                float overtone = 0.42f * Mathf.Sin(2.0f * Mathf.PI * 1320.0f * time);
                float chimeDelay = Mathf.Max(0.0f, time - 0.12f);
                float chime = time >= 0.12f
                    ? 0.5f * Mathf.Exp(-7.0f * chimeDelay) * Mathf.Sin(2.0f * Mathf.PI * 1760.0f * chimeDelay)
                    : 0.0f;
                samples[i] = 0.32f * attack * ((decay * (primary + overtone)) + chime);
            }

            AudioClip clip = AudioClip.Create("Mission5CompletionDing", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
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

        private static bool TryParseShadowRatioPacket(string packet, out float ratio)
        {
            packet = packet?.Trim();
            if (float.TryParse(packet, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio))
            {
                return true;
            }

            ratio = 0.0f;
            if (string.IsNullOrWhiteSpace(packet))
            {
                return false;
            }

            string[] values = packet.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                int equalsIndex = value.IndexOf('=');
                if (equalsIndex < 0)
                {
                    continue;
                }

                string key = value.Substring(0, equalsIndex).Trim();
                if (!string.Equals(key, "ratio", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(key, "shadow_ratio", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string rawNumber = value.Substring(equalsIndex + 1).Trim();
                return float.TryParse(rawNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio);
            }

            return false;
        }

        private static float GetFrameBlend(float speed)
        {
            if (speed <= 0.0f)
            {
                return 1.0f;
            }

            return 1.0f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        }

        private static float Clamp01(float value)
        {
            if (value <= 0.0f)
            {
                return 0.0f;
            }

            return value >= 1.0f ? 1.0f : value;
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
