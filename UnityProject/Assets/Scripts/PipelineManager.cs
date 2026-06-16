using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public class PipelineManager : MonoBehaviour
    {
        private const string DefaultCaptureCameraArguments = "--mode live --camera 0 --camera-width 640 --camera-height 360 --camera-fps 30 --camera-buffer-size 1 --camera-auto-exposure 0.75 --no-frame-enhance --no-control-window";
        private const string DefaultQwenServerArguments = "-m uvicorn app:app --host 127.0.0.1 --port 8000";
        private const string ShadowCaptureProcessLabel = "ShadowCapture";
        private const string QwenServerProcessLabel = "Qwen";
        private const string ShadowContourFileName = "shadow_contour.png";
        private const float ContourFileSettleDelaySeconds = 0.2f;
        private const float ContourFileReadyTimeoutSeconds = 5.0f;
        private const float ContourPollingIntervalSeconds = 0.25f;
        private const float QwenHealthCheckIntervalSeconds = 0.25f;
        private const int QwenHealthRequestTimeoutSeconds = 1;
        private const float QwenStartupTimeoutSeconds = 180.0f;
        private static readonly Vector2Int TerminalWindowOffset = new Vector2Int(40, 40);
        private static readonly Vector2Int TerminalWindowSize = new Vector2Int(980, 620);
        private static readonly Vector2Int TerminalWindowCascadeOffset = new Vector2Int(32, 32);

        [Header("Paths")]
        [SerializeField] private string pythonExecutablePath = @"C:\Users\creal\miniconda3\envs\artifact\python.exe";
        [SerializeField] private string qwenWorkingDirectory = @"C:\capstone\Shadow-to-3D-Generator\qwen";
        [SerializeField] private string qwenServerArguments = DefaultQwenServerArguments;
        [SerializeField] private string captureWorkingDirectory = @"C:\capstone\Shadow-to-3D-Generator";
        [SerializeField] private string captureScriptName = @"python\ShadowMesh.py";
        [SerializeField] private string captureArguments = DefaultCaptureCameraArguments;

        [SerializeField] private QwenClient qwenClient;
        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private ShadowMeshFileLoader meshFileLoader;
        [SerializeField] private OpeningVideoPlayer openingVideoPlayer;
        [SerializeField] private MainSubtitleController subtitleController;
        [SerializeField] private SoftWhiteCirclePlaneScaleAnimator mission1TransitionPlaneAnimator;
        [SerializeField] private SceneFlowController sceneFlowController;
        [SerializeField] private AudioSource openingSubtitleAudioSource;
        [SerializeField] private AudioClip openingSubtitleAudioClip;

        [SerializeField] private string mission1SceneName = "Mission1";

        private DateTime flowStartedUtc;
        private bool qwenServerReady;
        private bool qwenServerStarting;
        private bool qwenServerReadyLogged;
        private FileSystemWatcher contourWatcher;
        private readonly object pendingContourLock = new object();
        private string pendingContourPath;
        private DateTime? minimumAcceptedContourWriteTimeUtc;
        private DateTime lastPolledContourWriteTimeUtc = DateTime.MinValue;
        private float nextContourPollTime;
        private Coroutine keywordClassificationRoutine;
        private int terminalLaunchCount;
        private bool openingSubtitleAudioStarted;
        private float openingSubtitleAudioStartedAt;
        private float openingSubtitleAudioDuration;
        private bool openingVideoCompleted;
        private string pendingKeywordUntilOpeningVideoComplete;
        private readonly List<LaunchedProcess> launchedProcesses = new List<LaunchedProcess>();

        private void Start()
        {
            SubscribeEvents();
            SetupContourWatcher();
            StartCoroutine(PrepareQwenLabelerAtStartupRoutine());
            StartPipeline();
        }

        private void Update()
        {
            PollContourFileIfNeeded();
            StartPendingContourClassificationIfNeeded();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            DisposeContourWatcher();
            StopOpeningSubtitleAudio();
            StopLaunchedProcesses();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            DisposeContourWatcher();
            StopOpeningSubtitleAudio();
            StopLaunchedProcesses();
        }

        private void OnApplicationQuit()
        {
            StopOpeningSubtitleAudio();
            StopLaunchedProcesses();
        }

        public void StartPipeline()
        {
            if (stateManager == null)
            {
                Debug.LogWarning("PipelineManager: pipeline cannot start because GameStateManager is not assigned.");
                return;
            }

            flowStartedUtc = DateTime.UtcNow;
            ConfigureCaptureAcceptance(IsCaptureFileMode());
            stateManager.ResetForCapture();
            keywordClassificationRoutine = null;
            lock (pendingContourLock)
            {
                pendingContourPath = null;
            }

            qwenClient?.ResetKeyword();
            StopOpeningSubtitleAudio();
            openingVideoCompleted = false;
            pendingKeywordUntilOpeningVideoComplete = null;

            StartCoroutine(StartPipelineRoutine());
        }

        public void SkipOpeningVideo()
        {
            if (openingVideoPlayer == null)
            {
                openingVideoPlayer = GetComponent<OpeningVideoPlayer>();
            }

            if (openingVideoPlayer == null)
            {
                openingVideoPlayer = FindObjectOfType<OpeningVideoPlayer>();
            }

            openingVideoPlayer?.SkipPlayback();
            openingVideoCompleted = true;
            ApplyPendingKeywordAfterOpeningVideo();
        }

        private void ConfigureCaptureAcceptance(bool useExistingShadowMesh)
        {
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

            if (useExistingShadowMesh)
            {
                minimumAcceptedContourWriteTimeUtc = null;
                lastPolledContourWriteTimeUtc = DateTime.MinValue;
            }
            else
            {
                minimumAcceptedContourWriteTimeUtc = flowStartedUtc;
                string contourPath = GetShadowContourPath();
                lastPolledContourWriteTimeUtc = File.Exists(contourPath)
                    ? File.GetLastWriteTimeUtc(contourPath)
                    : DateTime.MinValue;
            }
        }

        private IEnumerator StartPipelineRoutine()
        {
            stateManager.OnOpeningStarted();
            yield return PlayOpeningVideoRoutine();
            openingVideoCompleted = true;
            ApplyPendingKeywordAfterOpeningVideo();

            bool isCaptureFileMode = IsCaptureFileMode();
            if (!isCaptureFileMode && !IsCameraAvailable(GetCaptureCameraId()))
            {
                Debug.LogWarning("PipelineManager: ShadowMesh camera was not reported by Unity; launching capture process anyway.");
            }

            if (isCaptureFileMode)
            {
                Debug.Log("PipelineManager: opening complete; loading existing shadow mesh.");
                meshFileLoader?.LoadExistingMesh();
                string contourPath = GetShadowContourPath();
                if (File.Exists(contourPath))
                {
                    QueueContourClassification(contourPath);
                }
                else
                {
                    ShowPipelineStatus($"file mode contour PNG was not found: {contourPath}");
                }
            }
            else
            {
                Debug.Log("PipelineManager: opening complete; launching shadow capture process.");
                LaunchCaptureProcess();
            }

        }

        private IEnumerator PrepareQwenLabelerAtStartupRoutine()
        {
            yield return EnsureQwenServerReady();
            if (!qwenServerReady || qwenClient == null)
            {
                yield break;
            }

            qwenClient.WarmupLabeler();
        }

        private IEnumerator PlayOpeningVideoRoutine()
        {
            if (openingVideoPlayer == null)
            {
                openingVideoPlayer = GetComponent<OpeningVideoPlayer>();
            }

            if (openingVideoPlayer == null)
            {
                openingVideoPlayer = FindObjectOfType<OpeningVideoPlayer>();
            }

            if (openingVideoPlayer == null)
            {
                Debug.LogWarning("PipelineManager: OpeningVideoPlayer is not assigned; skipping opening video.");
                yield break;
            }

            yield return openingVideoPlayer.PlayOpeningRoutine();
        }

        private void SubscribeEvents()
        {
            if (qwenClient != null)
            {
                qwenClient.KeywordClassified -= HandleKeywordClassified;
                qwenClient.KeywordClassified += HandleKeywordClassified;
            }
        }

        private void UnsubscribeEvents()
        {
            if (qwenClient != null)
            {
                qwenClient.KeywordClassified -= HandleKeywordClassified;
            }
        }

        private IEnumerator ClassifyKeywordThenStartMission1Routine(string contourPath)
        {
            yield return WaitForContourFileReady(contourPath);
            if (!ShouldAcceptContour(contourPath))
            {
                ShowPipelineStatus($"Qwen keyword classification skipped because contour PNG is not ready: {contourPath}");
                keywordClassificationRoutine = null;
                yield break;
            }

            yield return EnsureQwenLabelerReady();
            if (!qwenServerReady)
            {
                ShowPipelineStatus("Qwen API is not ready.");
                keywordClassificationRoutine = null;
                yield break;
            }

            if (qwenClient == null)
            {
                ShowPipelineStatus("Qwen client is not assigned.");
                keywordClassificationRoutine = null;
                yield break;
            }

            if (!qwenClient.IsLabelerReady)
            {
                ShowPipelineStatus("Qwen labeler warmup failed; keyword classification was skipped.");
                keywordClassificationRoutine = null;
                yield break;
            }

            qwenClient.ClassifySilhouette(contourPath);
            while (qwenClient.IsClassifying)
            {
                yield return null;
            }

            if (!qwenClient.HasKeyword)
            {
                ShowPipelineStatus("Qwen keyword classification did not return a keyword.");
                keywordClassificationRoutine = null;
                yield break;
            }

            yield return WaitForOpeningVideoCompleteRoutine();
            ApplyPendingKeywordAfterOpeningVideo();
            yield return WaitForKeywordSubtitleTimingRoutine();

            yield return HideKeywordSubtitleRoutine();
            yield return PlayMission1TransitionPlaneRoutine();

            stateManager?.OnMediaPipeTrackingStarted();
            LoadMission1Scene();
            keywordClassificationRoutine = null;
        }

        private IEnumerator HideKeywordSubtitleRoutine()
        {
            if (subtitleController == null)
            {
                subtitleController = FindObjectOfType<MainSubtitleController>();
            }

            if (subtitleController == null)
            {
                yield break;
            }

            yield return subtitleController.HideKeywordResultAndWait();
        }

        private IEnumerator PlayMission1TransitionPlaneRoutine()
        {
            if (mission1TransitionPlaneAnimator == null)
            {
                mission1TransitionPlaneAnimator = FindObjectOfType<SoftWhiteCirclePlaneScaleAnimator>();
            }

            if (mission1TransitionPlaneAnimator == null)
            {
                yield break;
            }

            mission1TransitionPlaneAnimator.SetDestroyTargetPlaneOnTargetScaleReached(false);
            yield return mission1TransitionPlaneAnimator.PlayAndWaitRoutine();
            mission1TransitionPlaneAnimator.KeepTargetPlaneUntilNextSceneFirstFrame();
        }

        private void LoadMission1Scene()
        {
            if (sceneFlowController == null)
            {
                sceneFlowController = FindObjectOfType<SceneFlowController>();
            }

            if (sceneFlowController == null)
            {
                if (!string.IsNullOrWhiteSpace(mission1SceneName))
                {
                    SceneManager.LoadScene(mission1SceneName);
                }

                return;
            }

            sceneFlowController.LoadScene(mission1SceneName);
        }

        private void HandleKeywordClassified(string keyword)
        {
            Debug.Log($"PipelineManager: Qwen keyword ready: {keyword}");
            if (!openingVideoCompleted)
            {
                pendingKeywordUntilOpeningVideoComplete = keyword;
                return;
            }

            ApplyKeywordPresentation(keyword);
        }

        private IEnumerator WaitForOpeningVideoCompleteRoutine()
        {
            while (!openingVideoCompleted)
            {
                yield return null;
            }
        }

        private void ApplyPendingKeywordAfterOpeningVideo()
        {
            if (string.IsNullOrWhiteSpace(pendingKeywordUntilOpeningVideoComplete))
            {
                return;
            }

            string keyword = pendingKeywordUntilOpeningVideoComplete;
            pendingKeywordUntilOpeningVideoComplete = null;
            ApplyKeywordPresentation(keyword);
        }

        private void ApplyKeywordPresentation(string keyword)
        {
            stateManager?.SetKeyword(keyword);
            PlayOpeningSubtitleAudio();
        }

        private IEnumerator WaitForKeywordSubtitleTimingRoutine()
        {
            if (!openingSubtitleAudioStarted)
            {
                PlayOpeningSubtitleAudio();
            }

            if (!openingSubtitleAudioStarted || openingSubtitleAudioDuration <= 0.0f)
            {
                yield break;
            }

            float elapsedSinceAudioStarted = Time.realtimeSinceStartup - openingSubtitleAudioStartedAt;
            float waitSeconds = Mathf.Max(0.0f, openingSubtitleAudioDuration - elapsedSinceAudioStarted);
            if (waitSeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(waitSeconds);
            }
        }

        private void PlayOpeningSubtitleAudio()
        {
            if (openingSubtitleAudioClip == null || openingSubtitleAudioStarted)
            {
                return;
            }

            AudioSource audioSource = ResolveOpeningSubtitleAudioSource();
            if (audioSource == null)
            {
                return;
            }

            if (openingSubtitleAudioClip.loadState != AudioDataLoadState.Loaded)
            {
                openingSubtitleAudioClip.LoadAudioData();
            }

            audioSource.Stop();
            audioSource.clip = openingSubtitleAudioClip;
            audioSource.Play();

            openingSubtitleAudioStarted = true;
            openingSubtitleAudioStartedAt = Time.realtimeSinceStartup;
            openingSubtitleAudioDuration = GetAudioClipDuration(openingSubtitleAudioClip, audioSource);
        }

        private AudioSource ResolveOpeningSubtitleAudioSource()
        {
            if (openingSubtitleAudioSource == null)
            {
                openingSubtitleAudioSource = GetComponent<AudioSource>();
            }

            if (openingSubtitleAudioSource == null)
            {
                openingSubtitleAudioSource = gameObject.AddComponent<AudioSource>();
            }

            openingSubtitleAudioSource.playOnAwake = false;
            openingSubtitleAudioSource.loop = false;
            openingSubtitleAudioSource.spatialBlend = 0.0f;
            return openingSubtitleAudioSource;
        }

        private void StopOpeningSubtitleAudio()
        {
            if (openingSubtitleAudioSource != null && openingSubtitleAudioSource.isPlaying)
            {
                openingSubtitleAudioSource.Stop();
            }

            openingSubtitleAudioStarted = false;
            openingSubtitleAudioStartedAt = 0.0f;
            openingSubtitleAudioDuration = 0.0f;
        }

        private static float GetAudioClipDuration(AudioClip clip, AudioSource audioSource)
        {
            if (clip == null)
            {
                return 0.0f;
            }

            float pitch = audioSource != null ? Mathf.Abs(audioSource.pitch) : 1.0f;
            return clip.length / Mathf.Max(0.01f, pitch);
        }

        private void LaunchCaptureProcess()
        {
            LaunchPythonScriptInTerminal(ShadowCaptureProcessLabel, captureWorkingDirectory, captureScriptName, captureArguments);
        }

        private void SetupContourWatcher()
        {
            DisposeContourWatcher();

            string outputDirectory = GetShadowOutputDirectory();
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                Debug.LogWarning("PipelineManager: shadow output directory is empty; contour watcher was not started.");
                return;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
                contourWatcher = new FileSystemWatcher(outputDirectory, ShadowContourFileName)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
                };
                contourWatcher.Changed += HandleContourFileEvent;
                contourWatcher.Created += HandleContourFileEvent;
                contourWatcher.Renamed += HandleContourFileRenamed;
                contourWatcher.EnableRaisingEvents = true;
                Debug.Log($"PipelineManager: watching contour PNG at {Path.Combine(outputDirectory, ShadowContourFileName)}");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning($"PipelineManager: contour watcher could not be started: {exception.Message}");
            }
        }

        private void DisposeContourWatcher()
        {
            if (contourWatcher == null)
            {
                return;
            }

            contourWatcher.EnableRaisingEvents = false;
            contourWatcher.Changed -= HandleContourFileEvent;
            contourWatcher.Created -= HandleContourFileEvent;
            contourWatcher.Renamed -= HandleContourFileRenamed;
            contourWatcher.Dispose();
            contourWatcher = null;
        }

        private void HandleContourFileEvent(object sender, FileSystemEventArgs eventArgs)
        {
            QueueContourClassification(eventArgs.FullPath);
        }

        private void HandleContourFileRenamed(object sender, RenamedEventArgs eventArgs)
        {
            QueueContourClassification(eventArgs.FullPath);
        }

        private void QueueContourClassification(string contourPath)
        {
            if (keywordClassificationRoutine != null)
            {
                return;
            }

            if (!ShouldAcceptContour(contourPath))
            {
                return;
            }

            lock (pendingContourLock)
            {
                pendingContourPath = contourPath;
            }
        }

        private void StartPendingContourClassificationIfNeeded()
        {
            if (keywordClassificationRoutine != null ||
                qwenClient == null ||
                qwenClient.HasKeyword)
            {
                return;
            }

            string contourPath = null;
            lock (pendingContourLock)
            {
                if (!string.IsNullOrEmpty(pendingContourPath))
                {
                    contourPath = pendingContourPath;
                    pendingContourPath = null;
                }
            }

            if (string.IsNullOrEmpty(contourPath))
            {
                return;
            }

            Debug.Log($"PipelineManager: contour PNG ready for Qwen: {contourPath}");
            keywordClassificationRoutine = StartCoroutine(ClassifyKeywordThenStartMission1Routine(contourPath));
        }

        private void PollContourFileIfNeeded()
        {
            if (Time.unscaledTime < nextContourPollTime)
            {
                return;
            }

            nextContourPollTime = Time.unscaledTime + ContourPollingIntervalSeconds;

            string contourPath = GetShadowContourPath();
            if (!ShouldAcceptContour(contourPath))
            {
                return;
            }

            DateTime writeTimeUtc = File.GetLastWriteTimeUtc(contourPath);
            if (writeTimeUtc <= lastPolledContourWriteTimeUtc)
            {
                return;
            }

            lastPolledContourWriteTimeUtc = writeTimeUtc;
            QueueContourClassification(contourPath);
        }

        private IEnumerator WaitForContourFileReady(string contourPath)
        {
            float deadline = Time.realtimeSinceStartup + ContourFileReadyTimeoutSeconds;
            long lastLength = -1;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (ShouldAcceptContour(contourPath))
                {
                    long currentLength = new FileInfo(contourPath).Length;
                    if (currentLength > 0 && currentLength == lastLength)
                    {
                        yield break;
                    }

                    lastLength = currentLength;
                }

                yield return new WaitForSecondsRealtime(ContourFileSettleDelaySeconds);
            }
        }

        private bool ShouldAcceptContour(string contourPath)
        {
            if (string.IsNullOrWhiteSpace(contourPath) || !File.Exists(contourPath))
            {
                return false;
            }

            FileInfo contourFile;
            try
            {
                contourFile = new FileInfo(contourPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return false;
            }

            if (!contourFile.Exists || contourFile.Length <= 0)
            {
                return false;
            }

            if (!minimumAcceptedContourWriteTimeUtc.HasValue)
            {
                return true;
            }

            return contourFile.LastWriteTimeUtc >= minimumAcceptedContourWriteTimeUtc.Value;
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

        private IEnumerator EnsureQwenLabelerReady()
        {
            yield return EnsureQwenServerReady();
            if (!qwenServerReady || qwenClient == null)
            {
                yield break;
            }

            if (!qwenClient.IsLabelerReady && !qwenClient.IsLabelerWarmingUp)
            {
                qwenClient.WarmupLabeler();
            }

            while (qwenClient.IsLabelerWarmingUp)
            {
                yield return null;
            }
        }

        private IEnumerator StartQwenServerRoutine()
        {
            if (qwenClient == null)
            {
                yield break;
            }

            if (qwenServerStarting)
            {
                while (qwenServerStarting && !qwenServerReady)
                {
                    yield return null;
                }

                yield break;
            }

            qwenServerStarting = true;
            yield return CheckQwenServerReady();
            if (qwenServerReady)
            {
                qwenServerStarting = false;
                yield break;
            }

            LaunchPythonCommandInTerminal(QwenServerProcessLabel, qwenWorkingDirectory, qwenServerArguments);
            yield return WaitForQwenServerReady();
            qwenServerStarting = false;
        }

        private IEnumerator EnsureQwenServerReady()
        {
            if (qwenServerReady)
            {
                yield break;
            }

            yield return StartQwenServerRoutine();
        }

        private IEnumerator WaitForQwenServerReady()
        {
            float startedAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startedAt < QwenStartupTimeoutSeconds)
            {
                yield return CheckQwenServerReady();
                if (qwenServerReady)
                {
                    yield break;
                }

                yield return new WaitForSecondsRealtime(QwenHealthCheckIntervalSeconds);
            }

            Debug.LogWarning("PipelineManager: Qwen API did not respond before timeout.");
        }

        private IEnumerator CheckQwenServerReady()
        {
            string healthUrl = $"{qwenClient.BaseUrl.TrimEnd('/')}/health";
            using UnityWebRequest request = UnityWebRequest.Get(healthUrl);
            request.timeout = QwenHealthRequestTimeoutSeconds;
            yield return request.SendWebRequest();

            qwenServerReady = request.result == UnityWebRequest.Result.Success;
            if (qwenServerReady)
            {
                LogQwenServerReadyOnce();
            }
        }

        private void LogQwenServerReadyOnce()
        {
            if (qwenServerReadyLogged)
            {
                return;
            }

            qwenServerReadyLogged = true;
            Debug.Log("PipelineManager: Qwen API is ready.");
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

            CameraPythonProcessCleanup.KillStaleCameraProcesses(processLabel, workingDirectory);

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

            string powershellArguments = $"-NoProfile -ExecutionPolicy Bypass -Command {EscapeWindowsArgument(command)}";
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
                WindowStyle = ProcessWindowStyle.Normal
            };

            var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                launchedProcesses.Add(new LaunchedProcess(processLabel, process));
                StartCoroutine(PositionTerminalWindowRoutine(process, processLabel, launchIndex));
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
                    windowHandle = WindowsDisplayUtility.FindWindowByTitle(processLabel);
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

            WindowsDisplayUtility.MoveWindowToBounds(windowHandle, new RectInt(x, y, width, height), true);
            Debug.Log($"{processLabel}: terminal window moved to {targetDescription} at ({x}, {y}) without activation.");
#else
            yield break;
#endif
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
                    string.Equals(launchedProcess.Label, QwenServerProcessLabel, StringComparison.Ordinal))
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

        private string GetShadowContourPath()
        {
            return Path.Combine(GetShadowOutputDirectory(), ShadowContourFileName);
        }

        private string GetShadowOutputDirectory()
        {
            return Path.GetFullPath(Path.Combine(captureWorkingDirectory, "output", "shadowmesh"));
        }

        private void ShowPipelineStatus(string message)
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

        private IEnumerator RestoreUnityWindowFocusRoutine()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Process unityProcess = Process.GetCurrentProcess();
                IntPtr unityWindowHandle = unityProcess.MainWindowHandle;
                unityProcess.Dispose();
                if (unityWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(unityWindowHandle, SwRestore);
                    SetForegroundWindow(unityWindowHandle);
                }

                yield return new WaitForSecondsRealtime(0.1f);
            }
#else
            yield break;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const short SwRestore = 9;
        private const short SwShowNoActivate = 4;
        private const int CreateNewConsole = 0x00000010;
        private const int StartfUseShowWindow = 0x00000001;
        private const int StartfUseSize = 0x00000002;
        private const int StartfUsePosition = 0x00000004;

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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
                wShowWindow = SwShowNoActivate
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
            return WindowsDisplayUtility.TryGetMonitorBoundsByDisplayNumber(
                DisplayRoutingSettings.TerminalWindowsDisplayNumber,
                useWorkArea: true,
                out bounds,
                out description);
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
#endif
    }
}
