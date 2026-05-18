using System;
using System.Collections;
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
        private const float ExportStatusDuration = 2.0f;
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
        private string exportStatusMessage = string.Empty;
        private float exportStatusUntil;
        private bool sf3dServerReady;
        private bool sf3dServerStarting;

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
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
        }

        public void StartPipeline()
        {
            if (stateManager == null)
            {
                Debug.LogWarning("PipelineManager: pipeline cannot start because GameStateManager is not assigned.");
                return;
            }

            handTrackingStartedForCurrentCapture = false;
            StartCoroutine(StartSf3dServerRoutine());

            if (IsCaptureFileMode())
            {
                meshFileLoader?.LoadExistingMesh();
            }
            else
            {
                stateManager.OnShadowCaptureStarted();
                LaunchCaptureProcess();
            }

            StartHandTrackingIfNeeded();
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
            LaunchHandTrackingProcess();

            if (mediaPipeReceiver != null)
            {
                mediaPipeReceiver.StartReceiver();
            }

            stateManager?.OnMediaPipeTrackingStarted();
        }

        private void RequestShadowSilhouetteExport()
        {
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
                    Debug.Log("PipelineManager: SF3D server is ready.");
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
                Arguments = $"-NoExit -ExecutionPolicy Bypass -Command {EscapeWindowsArgument(command)}",
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                process.WaitForExit(1000);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{processLabel}: Windows terminal launch failed: {exception.Message}");
            }
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
            exportStatusMessage = message;
            exportStatusUntil = Time.unscaledTime + ExportStatusDuration;
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
