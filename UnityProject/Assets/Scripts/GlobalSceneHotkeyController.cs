using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public sealed class GlobalSceneHotkeyController : MonoBehaviour
    {
        private static readonly string[] SceneOrder =
        {
            "Opening",
            "Mission1",
            "Mission2",
            "Mission3",
            "Mission4",
            "Mission5",
            "Ending"
        };

        private static GlobalSceneHotkeyController instance;
        private static bool restartInProgress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            GlobalSceneHotkeyController existing = FindObjectOfType<GlobalSceneHotkeyController>();
            if (existing != null)
            {
                instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            GameObject controllerObject = new GameObject("GlobalSceneHotkeyController");
            DontDestroyOnLoad(controllerObject);
            instance = controllerObject.AddComponent<GlobalSceneHotkeyController>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                RestartBuildForNextAudience();
                return;
            }

            if (Input.GetKeyDown(KeyCode.W))
            {
                AdvanceCurrentSceneOrPhase();
            }
        }

        private static void RestartBuildForNextAudience()
        {
            if (restartInProgress)
            {
                return;
            }

            restartInProgress = true;

#if UNITY_EDITOR
            LoadScene(SceneOrder[0]);
            restartInProgress = false;
#else
            if (!TryRestartStandalonePlayer())
            {
                restartInProgress = false;
                LoadScene(SceneOrder[0]);
            }
#endif
        }

#if !UNITY_EDITOR
        private static bool TryRestartStandalonePlayer()
        {
            string executablePath = ResolveCurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                Debug.LogWarning($"GlobalSceneHotkeyController: executable path was not found: {executablePath}");
                return false;
            }

            string workingDirectory = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                workingDirectory = Directory.GetCurrentDirectory();
            }

            int currentProcessId = Process.GetCurrentProcess().Id;
            string command =
                $"Wait-Process -Id {currentProcessId}; " +
                $"Start-Process -FilePath {QuotePowerShellArgument(executablePath)} " +
                $"-WorkingDirectory {QuotePowerShellArgument(workingDirectory)}";

            string launchArguments = BuildCurrentCommandLineArguments();
            if (!string.IsNullOrWhiteSpace(launchArguments))
            {
                command += $" -ArgumentList {QuotePowerShellArgument(launchArguments)}";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command {EscapeWindowsArgument(command)}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                Process.Start(startInfo);
                Application.Quit(0);
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                Debug.LogWarning($"GlobalSceneHotkeyController: failed to restart build: {exception.Message}");
                return false;
            }
        }

        private static string ResolveCurrentExecutablePath()
        {
            try
            {
                using (Process currentProcess = Process.GetCurrentProcess())
                {
                    currentProcess.Refresh();
                    string modulePath = currentProcess.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(modulePath) && File.Exists(modulePath))
                    {
                        return modulePath;
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                Debug.LogWarning($"GlobalSceneHotkeyController: current executable lookup failed: {exception.Message}");
            }

            string dataPath = Application.dataPath;
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                return string.Empty;
            }

            string dataDirectoryName = Path.GetFileName(dataPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(dataDirectoryName) ||
                !dataDirectoryName.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string playerName = dataDirectoryName.Substring(0, dataDirectoryName.Length - "_Data".Length);
            string playerDirectory = Directory.GetParent(dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(playerDirectory))
            {
                return string.Empty;
            }

            string inferredPath = Path.Combine(playerDirectory, playerName + ".exe");
            return File.Exists(inferredPath) ? inferredPath : string.Empty;
        }

        private static string BuildCurrentCommandLineArguments()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args == null || args.Length <= 1)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 1; i < args.Length; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteProcessArgument(args[i]));
            }

            return builder.ToString();
        }

        private static string QuoteProcessArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (value.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string EscapeWindowsArgument(string value)
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static string QuotePowerShellArgument(string value)
        {
            return $"'{value.Replace("'", "''")}'";
        }
#endif

        private void AdvanceCurrentSceneOrPhase()
        {
            if (TryAdvanceOpening() ||
                TryAdvanceMission1() ||
                TryAdvanceMission2() ||
                TryAdvanceMission3() ||
                TryAdvanceMission4() ||
                TryAdvanceMission5() ||
                TryAdvanceEnding())
            {
                return;
            }

            LoadNextSceneByActiveScene();
        }

        private static bool TryAdvanceOpening()
        {
            PipelineManager pipelineManager = FindObjectOfType<PipelineManager>();
            if (pipelineManager == null)
            {
                return false;
            }

            pipelineManager.DebugAdvanceToMission1();
            return true;
        }

        private static bool TryAdvanceMission1()
        {
            Mission1Controller controller = FindObjectOfType<Mission1Controller>();
            if (controller == null)
            {
                return false;
            }

            controller.DebugAdvance();
            return true;
        }

        private static bool TryAdvanceMission2()
        {
            Mission2StarMeshIntroAnimator controller = FindObjectOfType<Mission2StarMeshIntroAnimator>();
            if (controller == null)
            {
                return false;
            }

            controller.DebugAdvance();
            return true;
        }

        private static bool TryAdvanceMission3()
        {
            Mission3HologramDisplayLoader loader = FindObjectOfType<Mission3HologramDisplayLoader>();
            if (loader != null)
            {
                loader.DebugAdvance();
                return true;
            }

            HologramSwipeRotationSystem swipeSystem = FindObjectOfType<HologramSwipeRotationSystem>();
            if (swipeSystem == null)
            {
                return false;
            }

            swipeSystem.DebugAdvanceToNextScene();
            return true;
        }

        private static bool TryAdvanceMission4()
        {
            Mission4Controller controller = FindObjectOfType<Mission4Controller>();
            if (controller == null)
            {
                return false;
            }

            controller.DebugAdvance();
            return true;
        }

        private static bool TryAdvanceMission5()
        {
            Mission5Controller controller = FindObjectOfType<Mission5Controller>();
            if (controller != null)
            {
                controller.DebugAdvance();
                return true;
            }

            Mission5SeesawShadowSystem shadowSystem = FindObjectOfType<Mission5SeesawShadowSystem>();
            if (shadowSystem == null)
            {
                return false;
            }

            shadowSystem.DebugTriggerCompletion();
            return true;
        }

        private static bool TryAdvanceEnding()
        {
            EndingVideoSequenceController endingController = FindObjectOfType<EndingVideoSequenceController>();
            if (endingController == null)
            {
                return false;
            }

            endingController.SkipToEndingModel();
            return true;
        }

        private static void LoadNextSceneByActiveScene()
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            for (int i = 0; i < SceneOrder.Length - 1; i++)
            {
                if (string.Equals(SceneOrder[i], currentSceneName, System.StringComparison.Ordinal))
                {
                    LoadScene(SceneOrder[i + 1]);
                    return;
                }
            }
        }

        private static void LoadScene(string sceneName)
        {
            SceneFlowController sceneFlowController = FindObjectOfType<SceneFlowController>();
            if (sceneFlowController != null)
            {
                sceneFlowController.LoadScene(sceneName);
                return;
            }

            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
    }
}
