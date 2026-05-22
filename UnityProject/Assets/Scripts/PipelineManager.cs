using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public class PipelineManager : MonoBehaviour
    {
        private const string DefaultCaptureCameraArguments = "--mode live --camera 0";
        private const string DefaultHandTrackingCameraArguments = "--camera 1";
        private const string DefaultSf3dServerArguments = "-m uvicorn app:app --host 127.0.0.1 --port 8000";
        private const int ExportResolution = 1024;
        private const float Sf3dHealthCheckIntervalSeconds = 1.0f;
        private const float Sf3dStartupTimeoutSeconds = 60.0f;
        private static readonly Color ExportFillColor = Color.black;
        private static readonly Color ExportBackgroundColor = new Color(0f, 0f, 0f, 0f);

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

        private bool handTrackingStartedForCurrentCapture;
        private DateTime flowStartedUtc;
        private bool sf3dServerReady;
        private bool sf3dServerStarting;
        private bool sf3dServerReadyLogged;
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

            stateManager.ResetToIdle();
            handTrackingStartedForCurrentCapture = false;
            sf3dClient?.ResetSilhouetteLabel();

            bool isCaptureFileMode = IsCaptureFileMode();
            if (!isCaptureFileMode && !IsCameraAvailable(GetCaptureCameraId()))
            {
                Debug.LogWarning("PipelineManager: ShadowMesh camera was not found; capture process will not start.");
                stateManager.OnShadowMeshLoadFailed("ShadowMesh camera was not found.");
                return;
            }

            StartCoroutine(StartSf3dServerRoutine());
            StartCoroutine(WarmupLabelerWhenSf3dReady());

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

        private IEnumerator ClassifySilhouetteWhenSf3dReady()
        {
            if (sf3dClient == null)
            {
                yield break;
            }

            yield return EnsureSf3dServerReady();
            if (!sf3dServerReady)
            {
                yield break;
            }

            string contourPath = Path.Combine(captureWorkingDirectory, "output", "shadowmesh", "shadow_contour.png");
            sf3dClient.ClassifySilhouette(contourPath);
        }

        private void RequestShadowSilhouetteExport()
        {
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
            if (sf3dClient != null)
            {
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
                return;
            }

            if (shadowMeshDeformer.SaveSilhouetteToPng(
                    outputPath,
                    ExportResolution,
                    ExportFillColor,
                    ExportBackgroundColor))
            {
                ShowExportStatus($"Saved shadow image: {outputPath}");
                StartSf3dFromPng(outputPath);
            }
            else
            {
                ShowExportStatus("Shadow image export failed.");
            }
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

        private void StartSf3dFromPng(string pngPath)
        {
            if (sf3dClient == null)
            {
                Debug.LogWarning("PipelineManager: SF3D generation skipped because SF3DGenerationClient is not assigned.");
                return;
            }

            stateManager?.OnReconstructionStarted();
            StartCoroutine(GenerateFromPngWhenSf3dReady(pngPath));
        }

        private void HandleGlbGenerated(string glbPath)
        {
            stateManager?.OnHologramOutputStarted();
        }

        private void HandleSilhouetteClassified(string label)
        {
            Debug.Log($"PipelineManager: silhouette label ready for texture prompt: {label}");
            sf3dClient?.WarmupTexturePipeline();
        }

        private IEnumerator WarmupLabelerWhenSf3dReady()
        {
            if (sf3dClient == null)
            {
                yield break;
            }

            yield return EnsureSf3dServerReady();
            if (sf3dServerReady)
            {
                sf3dClient.WarmupLabeler();
            }
        }

        private void LaunchCaptureProcess()
        {
            LaunchPythonScriptInTerminal("ShadowCapture", captureWorkingDirectory, captureScriptName, captureArguments);
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

        private static int ParseCameraId(string arguments, int fallback)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return fallback;
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

            return fallback;
        }

        private static bool IsCameraAvailable(int cameraId)
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            return cameraId >= 0 && cameraId < devices.Length;
        }

        private void LaunchHandTrackingProcess()
        {
            LaunchPythonScriptInTerminal("HandTracking", handTrackingWorkingDirectory, handTrackingScriptName, handTrackingArguments);
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

            LaunchPythonCommandInTerminal("SF3DServer", sf3dWorkingDirectory, sf3dServerArguments);
            yield return WaitForSf3dServerReady();
            sf3dServerStarting = false;
        }

        private IEnumerator GenerateFromPngBytesWhenSf3dReady(byte[] pngBytes)
        {
            yield return EnsureSf3dServerReady();
            if (!sf3dServerReady)
            {
                ShowExportStatus("SF3D server is not ready.");
                yield break;
            }

            sf3dClient.GenerateFromPngBytes(pngBytes, exportFileName);
        }

        private IEnumerator GenerateFromPngWhenSf3dReady(string pngPath)
        {
            yield return EnsureSf3dServerReady();
            if (!sf3dServerReady)
            {
                ShowExportStatus("SF3D server is not ready.");
                yield break;
            }

            sf3dClient.GenerateFromPng(pngPath);
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

            Debug.LogWarning("PipelineManager: SF3D server did not respond before timeout.");
        }

        private IEnumerator CheckSf3dServerReady()
        {
            string healthUrl = $"{sf3dClient.BaseUrl.TrimEnd('/')}/health";
            using UnityWebRequest request = UnityWebRequest.Get(healthUrl);
            request.timeout = 2;
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
            Debug.Log("PipelineManager: SF3D server is ready.");
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

            string pythonPath = ResolvePythonPath(workingDirectory);
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

            string pythonPath = ResolvePythonPath(workingDirectory);
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

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -Command {EscapeWindowsArgument(command)}",
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                launchedProcesses.Add(new LaunchedProcess(processLabel, process));
                process.WaitForExit(1000);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{processLabel}: Windows terminal launch failed: {exception.Message}");
                process.Dispose();
            }
        }

        private void StopHandTrackingProcess()
        {
            mediaPipeReceiver?.StopReceiver();
            StopLaunchedProcesses("HandTracking");
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

        private string ResolvePythonPath(string workingDirectory)
        {
            return pythonExecutablePath;
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
