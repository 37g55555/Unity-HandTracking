using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public class PipelineManager : MonoBehaviour
    {
        private const string DefaultCaptureCameraArguments = "--mode live --camera 0 --bg";
        private const string DefaultHandTrackingCameraArguments = "--camera 1";
        private const string DefaultSf3dServerArguments = "-m uvicorn app:app --host 127.0.0.1 --port 8000";
        private const string ShadowCaptureProcessLabel = "ShadowCapture";
        private const string HandTrackingProcessLabel = "HandTracking";
        private const string Sf3dServerProcessLabel = "API";
        private const string ShadowContourFileName = "shadow_contour.png";
        private const int ExportResolution = 1024;
        private const float Sf3dHealthCheckIntervalSeconds = 0.25f;
        private const int Sf3dHealthRequestTimeoutSeconds = 1;
        private const float Sf3dStartupTimeoutSeconds = 180.0f;
        private static readonly Color ExportFillColor = Color.black;
        private static readonly Color ExportBackgroundColor = new Color(0f, 0f, 0f, 0f);
        private static readonly Vector2Int TerminalWindowOffset = new Vector2Int(40, 40);
        private static readonly Vector2Int TerminalWindowSize = new Vector2Int(980, 620);
        private static readonly Vector2Int TerminalWindowCascadeOffset = new Vector2Int(32, 32);

        [Header("Paths")]
        [SerializeField] private string exportFileName = "deformed_shadow.png";
        [SerializeField] private string pythonExecutablePath = @"D:\anaconda3\envs\artifact\python.exe";
        [SerializeField] private string sf3dWorkingDirectory = @"D:\Unity-HandTracking\sf3d";
        [SerializeField] private string sf3dServerArguments = DefaultSf3dServerArguments;
        [SerializeField] private string captureWorkingDirectory = @"D:\Unity-HandTracking";
        [SerializeField] private string captureScriptName = @"python\ShadowMesh.py";
        [SerializeField] private string captureArguments = DefaultCaptureCameraArguments;
        [SerializeField] private string handTrackingWorkingDirectory = @"D:\Unity-HandTracking";
        [SerializeField] private string handTrackingScriptName = @"python\MediaPipeTracking.py";
        [SerializeField] private string handTrackingArguments = DefaultHandTrackingCameraArguments;

        [SerializeField] private SF3DGenerationClient sf3dClient;
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private ShadowMeshFileLoader meshFileLoader;
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private ShadowMeshDeformer shadowMeshDeformer;
        [SerializeField] private SmokeTransitionEffect smokeTransitionEffect;

        private bool handTrackingStartedForCurrentCapture;
        private bool waitingForSmokeExit;
        private DateTime flowStartedUtc;
        private bool sf3dServerReady;
        private bool sf3dServerStarting;
        private bool sf3dServerReadyLogged;
        private int terminalLaunchCount;
        private readonly List<LaunchedProcess> launchedProcesses = new List<LaunchedProcess>();

        private void Start()
        {
            flowStartedUtc = DateTime.UtcNow;
            bool useExistingShadowMesh = IsCaptureFileMode();

            if (meshFileLoader != null)
            {
                meshFileLoader.SetLoadExistingMeshOnStart(useExistingShadowMesh);

                if (useExistingShadowMesh)
                {
                    meshFileLoader.ClearMinimumAcceptedMeshWriteTimeUtc();
                }
                else
                {
                    meshFileLoader.SetMinimumAcceptedMeshWriteTimeUtc(flowStartedUtc);
                }
            }

            SubscribeEvents();
            StartPipeline();
        }

        private void Update()
        {
            if (WasExportKeyPressed())
            {
                RequestShadowSilhouetteExport();
            }
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            StopLaunchedProcesses();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            StopLaunchedProcesses();
        }

        private void OnApplicationQuit()
        {
            StopLaunchedProcesses();
        }

        public void StartPipeline()
        {
            if (stateManager == null)
            {
                Debug.LogWarning("PipelineManager: pipeline cannot start because GameStateManager is not assigned.");
                return;
            }

            stateManager.ResetForCapture();
            handTrackingStartedForCurrentCapture = false;
            waitingForSmokeExit = false;
            sf3dClient?.ResetSilhouetteLabel();

            StartCoroutine(StartPipelineRoutine());
        }

        private IEnumerator StartPipelineRoutine()
        {
            yield return StartSf3dServerRoutine();
            if (sf3dServerReady && sf3dClient != null)
            {
                sf3dClient.WarmupLabeler();
            }

            bool isCaptureFileMode = IsCaptureFileMode();
            if (!isCaptureFileMode && !IsCameraAvailable(GetCaptureCameraId()))
            {
                Debug.LogWarning("PipelineManager: ShadowMesh camera was not found; capture process will not start.");
                stateManager.OnShadowMeshLoadFailed("ShadowMesh camera was not found.");
                yield break;
            }

            if (isCaptureFileMode)
            {
                meshFileLoader?.LoadExistingMesh();
            }
            else
            {
                stateManager.OnShadowCaptureStarted();
                LaunchCaptureProcess();
            }
        }

        private void SubscribeEvents()
        {
            if (stateManager != null)
            {
                stateManager.ShadowMeshLoaded -= HandleShadowMeshLoaded;
                stateManager.ShadowMeshLoaded += HandleShadowMeshLoaded;
            }

            if (sf3dClient != null)
            {
                sf3dClient.GlbGenerated -= HandleGlbGenerated;
                sf3dClient.GlbGenerated += HandleGlbGenerated;
                sf3dClient.SilhouetteClassified -= HandleSilhouetteClassified;
                sf3dClient.SilhouetteClassified += HandleSilhouetteClassified;
            }

            SubscribeSmokeTransitionExit();
        }

        private void UnsubscribeEvents()
        {
            if (stateManager != null)
            {
                stateManager.ShadowMeshLoaded -= HandleShadowMeshLoaded;
            }

            if (sf3dClient != null)
            {
                sf3dClient.GlbGenerated -= HandleGlbGenerated;
                sf3dClient.SilhouetteClassified -= HandleSilhouetteClassified;
            }

            UnsubscribeSmokeTransitionExit();
        }

        private void HandleShadowMeshLoaded(string path, int vertexCount, int boundaryCount)
        {
            if (handTrackingStartedForCurrentCapture)
            {
                return;
            }

            StartHandTrackingIfNeeded();
        }

        private void StartHandTrackingIfNeeded()
        {
            if (handTrackingStartedForCurrentCapture)
            {
                return;
            }

            handTrackingStartedForCurrentCapture = true;
            bool shouldStartHandTracking = IsCameraAvailable(GetHandTrackingCameraId());

            if (shouldStartHandTracking)
            {
                LaunchHandTrackingProcess();

                if (mediaPipeReceiver != null)
                {
                    mediaPipeReceiver.StartReceiver();
                }
            }
            else
            {
                Debug.Log("PipelineManager: MediaPipe camera was not found; skipping hand tracking.");
            }

            stateManager?.OnMediaPipeTrackingStarted();
            StartCoroutine(ClassifySilhouetteWhenSf3dReady());
        }

        private void RequestShadowSilhouetteExport()
        {
            if (stateManager == null ||
                stateManager.CurrentState != GameStateManager.PipelineState.MediaPipeTracking)
            {
                return;
            }

            if (sf3dClient != null && sf3dClient.IsClassifying)
            {
                ShowExportStatus("Silhouette classification is still running.");
                return;
            }

            if (sf3dClient != null && !sf3dClient.HasSilhouetteLabel)
            {
                ShowExportStatus("Silhouette classification is not ready.");
                return;
            }

            if (sf3dClient != null &&
                sf3dClient.IsRunning)
            {
                ShowExportStatus("SF3D generation is already running.");
                return;
            }

            ExportCurrentShadowSilhouette();
        }

        private void ExportCurrentShadowSilhouette()
        {
            stateManager?.OnMeshExtractionStarted();
            StopHandTrackingProcess();

            if (shadowMeshDeformer == null || !shadowMeshDeformer.HasMesh)
            {
                ShowExportStatus("Shadow mesh is not loaded.");
                Debug.LogWarning("PipelineManager: shadow export skipped because no mesh is loaded.");
                return;
            }

            string outputPath = GetSilhouetteExportPath();
            if (sf3dClient == null)
            {
                ShowExportStatus("SF3D client is not assigned.");
                return;
            }

            if (shadowMeshDeformer.TryEncodeSilhouetteToPng(
                    out byte[] pngBytes,
                    ExportResolution,
                    ExportFillColor,
                    ExportBackgroundColor))
            {
                if (!TrySaveSilhouettePng(outputPath, pngBytes))
                {
                    ShowExportStatus("Shadow image save failed.");
                    return;
                }

                ShowExportStatus($"Saved shadow image: {outputPath}");
                stateManager?.OnReconstructionStarted();
                StartCoroutine(GenerateFromPngBytesWhenSf3dReady(pngBytes));
                return;
            }

            ShowExportStatus("Shadow image export failed.");
        }

        private IEnumerator ClassifySilhouetteWhenSf3dReady()
        {
            yield return EnsureSf3dServerReady();
            if (!sf3dServerReady)
            {
                ShowExportStatus("API is not ready.");
                yield break;
            }

            if (sf3dClient == null)
            {
                ShowExportStatus("SF3D client is not assigned.");
                yield break;
            }

            while (sf3dClient.IsLabelerWarmingUp)
            {
                yield return null;
            }

            string contourPath = GetShadowContourPath();
            sf3dClient.ClassifySilhouette(contourPath);
        }

        private IEnumerator GenerateFromPngBytesWhenSf3dReady(byte[] pngBytes)
        {
            yield return EnsureSf3dServerReady();
            if (!sf3dServerReady)
            {
                ShowExportStatus("API is not ready.");
                yield break;
            }

            if (sf3dClient == null)
            {
                ShowExportStatus("SF3D client is not assigned.");
                yield break;
            }

            sf3dClient.GenerateFromPngBytes(pngBytes, exportFileName);
        }

        private bool TrySaveSilhouettePng(string outputPath, byte[] pngBytes)
        {
            try
            {
                string outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                File.WriteAllBytes(outputPath, pngBytes);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning($"PipelineManager: shadow image save failed: {exception.Message}");
                return false;
            }
        }

        private void HandleGlbGenerated(string glbPath)
        {
            bool shouldWaitForSmokeExit = smokeTransitionEffect != null && smokeTransitionEffect.isActiveAndEnabled;
            waitingForSmokeExit = shouldWaitForSmokeExit;
            stateManager?.OnHologramOutputStarted();

            if (!shouldWaitForSmokeExit)
            {
                sf3dClient?.LoadTargetSceneAfterGeneration();
            }
        }

        private void HandleSmokeExitCompleted()
        {
            if (!waitingForSmokeExit)
            {
                return;
            }

            waitingForSmokeExit = false;
            sf3dClient?.LoadTargetSceneAfterGeneration();
        }

        private void SubscribeSmokeTransitionExit()
        {
            if (smokeTransitionEffect == null)
            {
                return;
            }

            smokeTransitionEffect.ExitCompleted -= HandleSmokeExitCompleted;
            smokeTransitionEffect.ExitCompleted += HandleSmokeExitCompleted;
        }

        private void UnsubscribeSmokeTransitionExit()
        {
            if (smokeTransitionEffect == null)
            {
                return;
            }

            smokeTransitionEffect.ExitCompleted -= HandleSmokeExitCompleted;
        }

        private void HandleSilhouetteClassified(string label)
        {
            Debug.Log($"PipelineManager: silhouette label ready for texture prompt: {label}");
            sf3dClient?.WarmupTexturePipeline();
        }

        private void LaunchCaptureProcess()
        {
            LaunchPythonScriptInTerminal(ShadowCaptureProcessLabel, captureWorkingDirectory, captureScriptName, captureArguments);
        }

        private bool IsCaptureFileMode()
        {
            if (string.IsNullOrWhiteSpace(captureArguments))
            {
                return false;
            }

            return captureArguments.IndexOf("--mode file", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int GetCaptureCameraId()
        {
            return ParseCameraId(captureArguments, 0);
        }

        private int GetHandTrackingCameraId()
        {
            return ParseCameraId(handTrackingArguments, 1);
        }

        private static int ParseCameraId(string arguments, int defaultCameraId)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return defaultCameraId;
            }

            string[] tokens = arguments.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < tokens.Length; index++)
            {
                string token = tokens[index];
                if (token.Equals("--camera", StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < tokens.Length &&
                    int.TryParse(tokens[index + 1], out int spacedValue))
                {
                    return spacedValue;
                }

                const string cameraPrefix = "--camera=";
                if (token.StartsWith(cameraPrefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(token.Substring(cameraPrefix.Length), out int assignedValue))
                {
                    return assignedValue;
                }
            }

            return defaultCameraId;
        }

        private static bool IsCameraAvailable(int cameraId)
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            return cameraId >= 0 && cameraId < devices.Length;
        }

        private void LaunchHandTrackingProcess()
        {
            LaunchPythonScriptInTerminal(HandTrackingProcessLabel, handTrackingWorkingDirectory, handTrackingScriptName, handTrackingArguments);
        }

        private IEnumerator StartSf3dServerRoutine()
        {
            if (sf3dClient == null)
            {
                yield break;
            }

            if (sf3dServerStarting)
            {
                yield return WaitForSf3dServerReady();
                yield break;
            }

            sf3dServerStarting = true;
            yield return CheckSf3dServerReady();
            if (sf3dServerReady)
            {
                sf3dServerStarting = false;
                yield break;
            }

            LaunchPythonCommandInTerminal(Sf3dServerProcessLabel, sf3dWorkingDirectory, sf3dServerArguments);
            yield return WaitForSf3dServerReady();
            sf3dServerStarting = false;
        }

        private IEnumerator EnsureSf3dServerReady()
        {
            if (sf3dServerReady)
            {
                yield break;
            }

            yield return StartSf3dServerRoutine();
        }

        private IEnumerator WaitForSf3dServerReady()
        {
            float startedAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startedAt < Sf3dStartupTimeoutSeconds)
            {
                yield return CheckSf3dServerReady();
                if (sf3dServerReady)
                {
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Sf3dHealthCheckIntervalSeconds);
            }

            Debug.LogWarning("PipelineManager: API did not respond before timeout.");
        }

        private IEnumerator CheckSf3dServerReady()
        {
            string healthUrl = $"{sf3dClient.BaseUrl.TrimEnd('/')}/health";
            using UnityWebRequest request = UnityWebRequest.Get(healthUrl);
            request.timeout = Sf3dHealthRequestTimeoutSeconds;
            yield return request.SendWebRequest();

            sf3dServerReady = request.result == UnityWebRequest.Result.Success;
            if (sf3dServerReady)
            {
                LogSf3dServerReadyOnce();
            }
        }

        private void LogSf3dServerReadyOnce()
        {
            if (sf3dServerReadyLogged)
            {
                return;
            }

            sf3dServerReadyLogged = true;
            Debug.Log("PipelineManager: API is ready.");
        }

        private void LaunchPythonScriptInTerminal(
            string processLabel,
            string workingDirectory,
            string scriptName,
            string scriptArguments)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                Debug.LogWarning($"{processLabel}: working directory is empty.");
                return;
            }

            string pythonPath = pythonExecutablePath;
            string scriptPath = Path.Combine(workingDirectory, scriptName);
            if (!File.Exists(pythonPath))
            {
                Debug.LogWarning($"{processLabel}: python executable was not found: {pythonPath}");
                return;
            }

            if (!File.Exists(scriptPath))
            {
                Debug.LogWarning($"{processLabel}: script was not found: {scriptPath}");
                return;
            }

            LaunchProcessInWindowsTerminal(processLabel, workingDirectory, pythonPath, scriptPath, scriptArguments);
        }

        private void LaunchPythonCommandInTerminal(string processLabel, string workingDirectory, string pythonArguments)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                Debug.LogWarning($"{processLabel}: working directory is empty.");
                return;
            }

            if (!Directory.Exists(workingDirectory))
            {
                Debug.LogWarning($"{processLabel}: working directory was not found: {workingDirectory}");
                return;
            }

            string pythonPath = pythonExecutablePath;
            if (!File.Exists(pythonPath))
            {
                Debug.LogWarning($"{processLabel}: python executable was not found: {pythonPath}");
                return;
            }

            LaunchProcessInWindowsTerminal(processLabel, workingDirectory, pythonPath, string.Empty, pythonArguments);
        }

        private void LaunchProcessInWindowsTerminal(
            string processLabel,
            string workingDirectory,
            string pythonPath,
            string scriptPath,
            string scriptArguments)
        {
            string command =
                $"$Host.UI.RawUI.WindowTitle = {QuotePowerShellArgument(processLabel)}; " +
                $"Set-Location -LiteralPath {QuotePowerShellArgument(workingDirectory)}; " +
                $"& {QuotePowerShellArgument(pythonPath)}";

            if (!string.IsNullOrWhiteSpace(scriptPath))
            {
                command += $" {QuotePowerShellArgument(scriptPath)}";
            }

            if (!string.IsNullOrWhiteSpace(scriptArguments))
            {
                command += $" {scriptArguments}";
            }

            string powershellArguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -Command {EscapeWindowsArgument(command)}";
            int launchIndex = terminalLaunchCount++;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (TryLaunchPositionedWindowsTerminal(
                    processLabel,
                    workingDirectory,
                    powershellArguments,
                    launchIndex,
                    out Process positionedProcess))
            {
                launchedProcesses.Add(new LaunchedProcess(processLabel, positionedProcess));
                StartCoroutine(PositionTerminalWindowRoutine(positionedProcess, processLabel, launchIndex));
                positionedProcess.WaitForExit(1000);
                return;
            }
#endif

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = powershellArguments,
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Minimized
            };

            var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                launchedProcesses.Add(new LaunchedProcess(processLabel, process));
                StartCoroutine(PositionTerminalWindowRoutine(process, processLabel, launchIndex));
                process.WaitForExit(1000);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{processLabel}: Windows terminal launch failed: {exception.Message}");
                process.Dispose();
            }
        }

        private IEnumerator PositionTerminalWindowRoutine(Process process, string processLabel, int launchIndex)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IntPtr windowHandle = IntPtr.Zero;
            float deadline = Time.realtimeSinceStartup + 3f;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (process == null || process.HasExited)
                {
                    yield break;
                }

                process.Refresh();
                windowHandle = process.MainWindowHandle;
                if (windowHandle == IntPtr.Zero)
                {
                    windowHandle = FindWindow(null, processLabel);
                }

                if (windowHandle != IntPtr.Zero)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(0.05f);
            }

            if (windowHandle == IntPtr.Zero)
            {
                Debug.LogWarning($"{processLabel}: terminal window handle was not found; window position was not changed.");
                yield break;
            }

            if (!TryGetTerminalWindowPlacement(
                    launchIndex,
                    out int x,
                    out int y,
                    out int width,
                    out int height,
                    out string targetDescription))
            {
                Debug.LogWarning($"{processLabel}: target terminal display was not found; window position was not changed.");
                yield break;
            }

            MoveWindow(windowHandle, x, y, width, height, true);
            Debug.Log($"{processLabel}: terminal window moved to {targetDescription} at ({x}, {y}) without activation.");
#else
            yield break;
#endif
        }

        private void StopHandTrackingProcess()
        {
            mediaPipeReceiver?.StopReceiver();
            StopLaunchedProcesses(HandTrackingProcessLabel);
        }

        private void StopLaunchedProcesses()
        {
            StopLaunchedProcesses(null);
        }

        private void StopLaunchedProcesses(string processLabel)
        {
            for (int index = launchedProcesses.Count - 1; index >= 0; index--)
            {
                LaunchedProcess launchedProcess = launchedProcesses[index];
                if (string.IsNullOrEmpty(processLabel) &&
                    string.Equals(launchedProcess.Label, Sf3dServerProcessLabel, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(processLabel) &&
                    !string.Equals(launchedProcess.Label, processLabel, StringComparison.Ordinal))
                {
                    continue;
                }

                launchedProcesses.RemoveAt(index);
                Process process = launchedProcess.Process;

                try
                {
                    if (process == null || process.HasExited)
                    {
                        process?.Dispose();
                        continue;
                    }

                    KillProcessTree(process.Id);
                    process.Dispose();
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
                {
                    Debug.LogWarning($"PipelineManager: launched process cleanup failed: {exception.Message}");
                    process.Dispose();
                }
            }
        }

        private readonly struct LaunchedProcess
        {
            public LaunchedProcess(string label, Process process)
            {
                Label = label;
                Process = process;
            }

            public string Label { get; }
            public Process Process { get; }
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

        private string GetSilhouetteExportPath()
        {
            string baseDirectory = captureWorkingDirectory;

            string outputDirectory = Path.Combine(baseDirectory, "output", "sf3d");
            return Path.Combine(outputDirectory, exportFileName);
        }

        private string GetShadowContourPath()
        {
            string baseDirectory = captureWorkingDirectory;

            string outputDirectory = Path.Combine(baseDirectory, "output", "shadowmesh");
            return Path.Combine(outputDirectory, ShadowContourFileName);
        }

        private static bool WasExportKeyPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return keyboard.enterKey.wasPressedThisFrame ||
                   keyboard.numpadEnterKey.wasPressedThisFrame;
        }

        private void ShowExportStatus(string message)
        {
            Debug.Log($"PipelineManager: {message}");
        }

        private static string EscapeWindowsArgument(string value)
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static string QuotePowerShellArgument(string value)
        {
            return $"'{value.Replace("'", "''")}'";
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const short SwShowMinNoActive = 7;
        private const int CreateNewConsole = 0x00000010;
        private const int StartfUseShowWindow = 0x00000001;
        private const int StartfUseSize = 0x00000002;
        private const int StartfUsePosition = 0x00000004;

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        private readonly struct MonitorWorkArea
        {
            public MonitorWorkArea(IntPtr handle, RectInt bounds)
            {
                Handle = handle;
                Bounds = bounds;
            }

            public IntPtr Handle { get; }
            public RectInt Bounds { get; }
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool repaint);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            int dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref StartupInfo lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private bool TryLaunchPositionedWindowsTerminal(
            string processLabel,
            string workingDirectory,
            string powershellArguments,
            int launchIndex,
            out Process process)
        {
            process = null;
            if (!TryGetTerminalWindowPlacement(
                    launchIndex,
                    out int x,
                    out int y,
                    out int width,
                    out int height,
                    out string targetDescription))
            {
                return false;
            }

            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf(typeof(StartupInfo)),
                lpTitle = processLabel,
                dwX = x,
                dwY = y,
                dwXSize = width,
                dwYSize = height,
                dwFlags = StartfUsePosition | StartfUseSize | StartfUseShowWindow,
                wShowWindow = SwShowMinNoActive
            };

            string commandLineText = $"powershell.exe {powershellArguments}";
            var commandLine = new StringBuilder(commandLineText);
            bool launched = CreateProcess(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateNewConsole,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfo,
                out ProcessInformation processInformation);

            if (!launched)
            {
                Debug.LogWarning($"{processLabel}: positioned terminal launch failed: {new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message}");
                return false;
            }

            CloseHandle(processInformation.hThread);
            CloseHandle(processInformation.hProcess);

            process = Process.GetProcessById(processInformation.dwProcessId);
            Debug.Log($"{processLabel}: terminal created on {targetDescription} at ({x}, {y}).");
            return true;
        }

        private bool TryGetTerminalTargetWorkArea(out RectInt bounds, out string description)
        {
            List<MonitorWorkArea> monitors = GetMonitorWorkAreas();
            if (monitors.Count == 0)
            {
                bounds = default;
                description = "no monitor";
                return false;
            }

            int targetMonitorIndex = monitors.Count - 1;
            bounds = monitors[targetMonitorIndex].Bounds;
            description = $"display {targetMonitorIndex}";
            return true;
        }

        private bool TryGetTerminalWindowPlacement(
            int launchIndex,
            out int x,
            out int y,
            out int width,
            out int height,
            out string targetDescription)
        {
            if (!TryGetTerminalTargetWorkArea(out RectInt displayBounds, out targetDescription))
            {
                x = 0;
                y = 0;
                width = 0;
                height = 0;
                return false;
            }

            width = Mathf.Clamp(TerminalWindowSize.x, 320, displayBounds.width);
            height = Mathf.Clamp(TerminalWindowSize.y, 240, displayBounds.height);
            int cascadeX = TerminalWindowCascadeOffset.x * (launchIndex % 6);
            int cascadeY = TerminalWindowCascadeOffset.y * (launchIndex % 6);
            int maxX = Mathf.Max(0, displayBounds.width - width);
            int maxY = Mathf.Max(0, displayBounds.height - height);
            x = displayBounds.x + Mathf.Clamp(TerminalWindowOffset.x + cascadeX, 0, maxX);
            y = displayBounds.y + Mathf.Clamp(TerminalWindowOffset.y + cascadeY, 0, maxY);
            return true;
        }

        private static List<MonitorWorkArea> GetMonitorWorkAreas()
        {
            var monitors = new List<MonitorWorkArea>();
            EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (monitor, _, _, _) =>
                {
                    var info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        NativeRect work = info.rcWork;
                        monitors.Add(new MonitorWorkArea(
                            monitor,
                            new RectInt(
                                work.left,
                                work.top,
                                work.right - work.left,
                                work.bottom - work.top)));
                    }

                    return true;
                },
                IntPtr.Zero);
            return monitors;
        }
#endif
    }
}
