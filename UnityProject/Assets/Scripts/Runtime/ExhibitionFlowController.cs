using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public class ExhibitionFlowController : MonoBehaviour
    {
        private const string DefaultIpCameraUrl = "http://192.168.0.12:8081/video";

        [Header("Flow")]
        [SerializeField] private bool autoStartOnPlay = true;
        [SerializeField] private bool suppressExistingMeshOnPlay = true;
        [SerializeField] private bool autoStartHandTrackingAfterMeshLoad = true;
        [SerializeField] private bool stopProcessesOnDisable = true;
        [SerializeField] private bool useExternalBackendOnWindows = true;

        [Header("Export")]
        [SerializeField] private bool allowManualPngExport = true;
        [SerializeField] private string exportFileName = "deformed_shadow.png";
        [SerializeField] private int exportResolution = 1024;
        [SerializeField] private Color exportFillColor = default;
        [SerializeField] private Color exportBackgroundColor = new Color(0f, 0f, 0f, 0f);

        [Header("Export Feedback")]
        [SerializeField] private float exportStatusDuration = 2.0f;

        [Header("Capture")]
        [SerializeField] private bool launchCaptureInTerminal = true;
        [SerializeField] private string captureWorkingDirectory = string.Empty;
        [SerializeField] private string capturePythonRelativePath = ".venv/bin/python";
        [SerializeField] private string captureScriptName = "shadow_capture.py";
        [SerializeField] private string captureArguments = "--mode live --camera-url http://192.168.0.12:8081/video";

        [Header("Hand Tracking")]
        [SerializeField] private bool launchHandTrackingInTerminal = true;
        [SerializeField] private string handTrackingWorkingDirectory = string.Empty;
        [SerializeField] private string handTrackingPythonRelativePath = ".venv/bin/python";
        [SerializeField] private string handTrackingScriptName = "main.py";
        [SerializeField] private string handTrackingArguments = "--camera-url http://192.168.0.12:8081/video --udp-host 127.0.0.1";

        [Header("Dependencies")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private LiveMeshLoader liveMeshLoader;
        [SerializeField] private HandLandmarkUdpReceiver handLandmarkUdpReceiver;
        [SerializeField] private MediaPipeScaleInput mediaPipeScaleInput;
        [SerializeField] private ShadowDeformer shadowDeformer;

        private readonly ConcurrentQueue<string> pendingLogs = new ConcurrentQueue<string>();

        private Process captureProcess;
        private Process handTrackingProcess;
        private bool handTrackingStartedForCurrentCapture;
        private DateTime flowStartedUtc;
        private string exportStatusMessage = string.Empty;
        private float exportStatusUntil;

        public void Configure(
            GameManager manager,
            LiveMeshLoader meshLoader,
            HandLandmarkUdpReceiver udpReceiver,
            MediaPipeScaleInput scaleInput)
        {
            gameManager = manager;
            liveMeshLoader = meshLoader;
            handLandmarkUdpReceiver = udpReceiver;
            mediaPipeScaleInput = scaleInput;

            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(captureWorkingDirectory))
            {
                captureWorkingDirectory = Path.Combine(userHome, "Downloads", "CAP_II");
            }

            if (string.IsNullOrWhiteSpace(handTrackingWorkingDirectory))
            {
                handTrackingWorkingDirectory = Path.Combine(userHome, "Downloads", "Unity-HandTracking-master", "3d Hand Tracking");
            }

            ApplyDefaultCameraArguments();

            flowStartedUtc = DateTime.UtcNow;
            if (IsExternalBackendModeForCurrentPlatform())
            {
                suppressExistingMeshOnPlay = false;
            }

            if (suppressExistingMeshOnPlay && liveMeshLoader != null)
            {
                liveMeshLoader.SetLoadExistingMeshOnStart(false);
                liveMeshLoader.SetMinimumAcceptedMeshWriteTimeUtc(flowStartedUtc);
            }
            else if (liveMeshLoader != null)
            {
                liveMeshLoader.SetLoadExistingMeshOnStart(true);
                liveMeshLoader.ClearMinimumAcceptedMeshWriteTimeUtc();
            }

            if (mediaPipeScaleInput != null)
            {
                mediaPipeScaleInput.enabled = false;
            }
        }

        private void ApplyDefaultCameraArguments()
        {
            string trimmedCaptureArguments = captureArguments == null ? string.Empty : captureArguments.Trim();
            if (string.IsNullOrWhiteSpace(trimmedCaptureArguments) ||
                trimmedCaptureArguments == "--mode live" ||
                trimmedCaptureArguments == "--mode live --camera 0 --no-camera-fallback")
            {
                captureArguments = $"--mode live --camera-url {DefaultIpCameraUrl}";
            }

            string trimmedHandTrackingArguments = handTrackingArguments == null ? string.Empty : handTrackingArguments.Trim();
            if (string.IsNullOrWhiteSpace(trimmedHandTrackingArguments) ||
                trimmedHandTrackingArguments == "--camera 1 --no-camera-fallback")
            {
                handTrackingArguments = $"--camera-url {DefaultIpCameraUrl} --udp-host 127.0.0.1";
            }
        }

        private void Start()
        {
            ResolveDependencies();
            SubscribeEvents();

            if (autoStartOnPlay)
            {
                StartExhibitionFlow();
            }
        }

        private void Update()
        {
            FlushPendingLogs();
            CheckProcessExit(ref captureProcess, "ShadowCapture");
            CheckProcessExit(ref handTrackingProcess, "HandTracking");

            if (allowManualPngExport && WasExportKeyPressed())
            {
                ExportCurrentShadowSilhouette();
            }
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(exportStatusMessage) || Time.unscaledTime > exportStatusUntil)
            {
                return;
            }

            const float width = 520.0f;
            const float height = 46.0f;
            Rect rect = new Rect((Screen.width - width) * 0.5f, 24.0f, width, height);
            GUI.Box(rect, exportStatusMessage);
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            if (stopProcessesOnDisable)
            {
                StopTrackedProcesses();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            if (stopProcessesOnDisable)
            {
                StopTrackedProcesses();
            }
        }

        public void StartExhibitionFlow()
        {
            ResolveDependencies();

            if (gameManager == null)
            {
                Debug.LogWarning("ExhibitionFlowController could not start because GameManager is missing.");
                return;
            }

            handTrackingStartedForCurrentCapture = false;
            if (mediaPipeScaleInput != null)
            {
                mediaPipeScaleInput.enabled = false;
            }

            gameManager.OnShadowCaptureStarted();
            if (ShouldLaunchBackendProcesses())
            {
                LaunchCaptureProcess();
            }
            else
            {
                Debug.Log("External backend mode is active. Unity will not launch ShadowCapture; waiting for OBJ output.");
            }
        }

        private void SubscribeEvents()
        {
            if (gameManager != null)
            {
                gameManager.ShadowMeshLoaded -= HandleShadowMeshLoaded;
                gameManager.ShadowMeshLoaded += HandleShadowMeshLoaded;
            }
        }

        private void UnsubscribeEvents()
        {
            if (gameManager != null)
            {
                gameManager.ShadowMeshLoaded -= HandleShadowMeshLoaded;
            }
        }

        private void HandleShadowMeshLoaded(string path, int vertexCount, int boundaryCount)
        {
            if (!autoStartHandTrackingAfterMeshLoad || handTrackingStartedForCurrentCapture)
            {
                return;
            }

            handTrackingStartedForCurrentCapture = true;
            if (ShouldLaunchBackendProcesses())
            {
                LaunchHandTrackingProcess();
            }
            else
            {
                Debug.Log("External backend mode is active. Unity will not launch HandTracking; listening for UDP packets.");
            }

            if (handLandmarkUdpReceiver != null)
            {
                handLandmarkUdpReceiver.StartReceiver();
            }

            if (mediaPipeScaleInput != null)
            {
                // Hand tracking should only deform mesh vertices in this prototype.
                // Keep root transform scale/position control disabled so GRAB never resizes the object.
                mediaPipeScaleInput.enabled = false;
            }

            gameManager?.OnHandTrackingStarted();
        }

        private bool ShouldLaunchBackendProcesses()
        {
            return !IsExternalBackendModeForCurrentPlatform();
        }

        private bool IsExternalBackendModeForCurrentPlatform()
        {
            return useExternalBackendOnWindows &&
                   (Application.platform == RuntimePlatform.WindowsEditor ||
                    Application.platform == RuntimePlatform.WindowsPlayer);
        }

        private void ExportCurrentShadowSilhouette()
        {
            ResolveDependencies();

            if (shadowDeformer == null || !shadowDeformer.HasMesh)
            {
                ShowExportStatus("PNG export skipped: no loaded shadow mesh.");
                Debug.LogWarning("Shadow silhouette export skipped because there is no loaded mesh.");
                return;
            }

            string outputPath = GetSilhouetteExportPath();
            Debug.Log($"Shadow silhouette export requested: {outputPath}");
            if (shadowDeformer.SaveSilhouetteToPng(
                    outputPath,
                    exportResolution,
                    exportFillColor == default ? Color.black : exportFillColor,
                    exportBackgroundColor))
            {
                ShowExportStatus($"Saved PNG: {outputPath}");
                gameManager?.OnShadowMeshExtracted();
            }
            else
            {
                ShowExportStatus("PNG export failed. Check Unity Console.");
            }
        }

        private void LaunchCaptureProcess()
        {
            StopProcess(ref captureProcess, "ShadowCapture");

            if (launchCaptureInTerminal)
            {
                LaunchProcessInTerminal("ShadowCapture", captureWorkingDirectory, capturePythonRelativePath, captureScriptName, captureArguments);
                return;
            }

            captureProcess = StartProcess(
                "ShadowCapture",
                captureWorkingDirectory,
                capturePythonRelativePath,
                captureScriptName,
                captureArguments);
        }

        private void LaunchHandTrackingProcess()
        {
            StopProcess(ref handTrackingProcess, "HandTracking");

            if (launchHandTrackingInTerminal)
            {
                LaunchProcessInTerminal("HandTracking", handTrackingWorkingDirectory, handTrackingPythonRelativePath, handTrackingScriptName, handTrackingArguments);
                return;
            }

            handTrackingProcess = StartProcess(
                "HandTracking",
                handTrackingWorkingDirectory,
                handTrackingPythonRelativePath,
                handTrackingScriptName,
                handTrackingArguments);
        }

        private Process StartProcess(
            string processLabel,
            string workingDirectory,
            string pythonRelativePath,
            string scriptName,
            string scriptArguments)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                Debug.LogWarning($"{processLabel} working directory is empty.");
                return null;
            }

            string pythonPath = ResolvePythonPath(workingDirectory, pythonRelativePath);
            string scriptPath = Path.Combine(workingDirectory, scriptName);
            if (!File.Exists(pythonPath))
            {
                Debug.LogWarning($"{processLabel} python executable was not found: {pythonPath}");
                return null;
            }

            if (!File.Exists(scriptPath))
            {
                Debug.LogWarning($"{processLabel} script was not found: {scriptPath}");
                return null;
            }

            string arguments = $"\"{scriptPath}\"";
            if (!string.IsNullOrWhiteSpace(scriptArguments))
            {
                arguments += $" {scriptArguments}";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    pendingLogs.Enqueue($"[{processLabel}] {eventArgs.Data}");
                }
            };

            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    pendingLogs.Enqueue($"[{processLabel}][ERR] {eventArgs.Data}");
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Debug.Log($"{processLabel} process started: {pythonPath} {arguments}");
                return process;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{processLabel} process failed to start: {exception.Message}");
                process.Dispose();
                return null;
            }
        }

        private void LaunchProcessInTerminal(
            string processLabel,
            string workingDirectory,
            string pythonRelativePath,
            string scriptName,
            string scriptArguments)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                Debug.LogWarning($"{processLabel} working directory is empty.");
                return;
            }

            string pythonPath = ResolvePythonPath(workingDirectory, pythonRelativePath);
            string scriptPath = Path.Combine(workingDirectory, scriptName);
            if (!File.Exists(pythonPath))
            {
                Debug.LogWarning($"{processLabel} python executable was not found: {pythonPath}");
                return;
            }

            if (!File.Exists(scriptPath))
            {
                Debug.LogWarning($"{processLabel} script was not found: {scriptPath}");
                return;
            }

            if (Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                LaunchProcessInWindowsTerminal(processLabel, workingDirectory, pythonPath, scriptPath, scriptArguments);
                return;
            }

            string shellCommand =
                $"cd {EscapeShellArgument(workingDirectory)} && " +
                $"{EscapeShellArgument(pythonPath)} {EscapeShellArgument(scriptPath)}";

            if (!string.IsNullOrWhiteSpace(scriptArguments))
            {
                shellCommand += $" {scriptArguments}";
            }

            string escapedCommand = EscapeAppleScriptString(shellCommand);
            string appleScriptArgs =
                $"-e \"tell application \\\"Terminal\\\" to activate\" " +
                $"-e \"tell application \\\"Terminal\\\" to do script \\\"{escapedCommand}\\\"\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = appleScriptArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false
            };

            var process = new Process
            {
                StartInfo = startInfo
            };

            try
            {
                process.Start();
                process.WaitForExit(1000);
                Debug.Log($"{processLabel} launched in Terminal: {shellCommand}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{processLabel} terminal launch failed: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        private void LaunchProcessInWindowsTerminal(
            string processLabel,
            string workingDirectory,
            string pythonPath,
            string scriptPath,
            string scriptArguments)
        {
            string arguments =
                $"/c start \"{processLabel}\" /D {EscapeWindowsArgument(workingDirectory)} " +
                $"{EscapeWindowsArgument(pythonPath)} {EscapeWindowsArgument(scriptPath)}";

            if (!string.IsNullOrWhiteSpace(scriptArguments))
            {
                arguments += $" {scriptArguments}";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                process.WaitForExit(1000);
                Debug.Log($"{processLabel} launched in Windows terminal: {pythonPath} \"{scriptPath}\" {scriptArguments}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{processLabel} Windows terminal launch failed: {exception.Message}");
            }
        }

        private void ResolveDependencies()
        {
            if (gameManager == null)
            {
                gameManager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            }

            if (liveMeshLoader == null)
            {
                liveMeshLoader = UnityEngine.Object.FindAnyObjectByType<LiveMeshLoader>();
            }

            if (handLandmarkUdpReceiver == null)
            {
                handLandmarkUdpReceiver = UnityEngine.Object.FindAnyObjectByType<HandLandmarkUdpReceiver>();
            }

            if (mediaPipeScaleInput == null)
            {
                mediaPipeScaleInput = UnityEngine.Object.FindAnyObjectByType<MediaPipeScaleInput>();
            }

            if (shadowDeformer == null)
            {
                shadowDeformer = UnityEngine.Object.FindAnyObjectByType<ShadowDeformer>();
            }
        }

        private string GetSilhouetteExportPath()
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string baseDirectory = captureWorkingDirectory;
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Path.Combine(userHome, "Downloads", "CAP_II");
            }

            string outputDirectory = Path.Combine(baseDirectory, "output");
            return Path.Combine(outputDirectory, exportFileName);
        }

        private static bool WasExportKeyPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return keyboard.enterKey.wasPressedThisFrame ||
                   keyboard.numpadEnterKey.wasPressedThisFrame ||
                   keyboard.sKey.wasPressedThisFrame;
        }

        private void ShowExportStatus(string message)
        {
            exportStatusMessage = message;
            exportStatusUntil = Time.unscaledTime + exportStatusDuration;
            Debug.Log(message);
        }

        private void FlushPendingLogs()
        {
            while (pendingLogs.TryDequeue(out string message))
            {
                Debug.Log(message);
            }
        }

        private void CheckProcessExit(ref Process process, string processLabel)
        {
            if (process == null || !process.HasExited)
            {
                return;
            }

            Debug.Log($"{processLabel} process exited with code {process.ExitCode}.");
            process.Dispose();
            process = null;
        }

        private void StopTrackedProcesses()
        {
            StopProcess(ref captureProcess, "ShadowCapture");
            StopProcess(ref handTrackingProcess, "HandTracking");
            StopProcessByScriptPath(captureWorkingDirectory, captureScriptName, "ShadowCapture");
            StopProcessByScriptPath(handTrackingWorkingDirectory, handTrackingScriptName, "HandTracking");
        }

        private static void StopProcess(ref Process process, string processLabel)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{processLabel} process could not be stopped cleanly: {exception.Message}");
            }
            finally
            {
                process.Dispose();
                process = null;
            }
        }

        private static void StopProcessByScriptPath(string workingDirectory, string scriptName, string processLabel)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(scriptName))
            {
                return;
            }

            string scriptPath = Path.Combine(workingDirectory, scriptName);
            if (!File.Exists(scriptPath))
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/pkill",
                Arguments = $"-f \"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                process.WaitForExit(1000);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{processLabel} background process stop failed: {exception.Message}");
            }
        }

        private static string ResolvePythonPath(string workingDirectory, string preferredRelativePath)
        {
            string preferredPath = Path.Combine(workingDirectory, preferredRelativePath);
            if (File.Exists(preferredPath))
            {
                return preferredPath;
            }

            string[] fallbackRelativePaths =
            {
                Path.Combine(".venv", "Scripts", "python.exe"),
                Path.Combine(".venv", "bin", "python"),
                "python.exe",
                "python3",
                "python"
            };

            foreach (string relativePath in fallbackRelativePaths)
            {
                string candidatePath = Path.Combine(workingDirectory, relativePath);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return preferredPath;
        }

        private static string EscapeShellArgument(string value)
        {
            return $"'{value.Replace("'", "'\"'\"'")}'";
        }

        private static string EscapeWindowsArgument(string value)
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static string EscapeAppleScriptString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
