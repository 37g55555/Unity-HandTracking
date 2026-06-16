using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public sealed class MediaPipeTrackingProcessLauncher : MonoBehaviour
    {
        private const string ProcessLabel = "HandTracking";

        [SerializeField] private bool launchOnStart = true;
        [SerializeField] private bool stopProcessOnDisable = true;
        [SerializeField] private string pythonExecutablePath = @"C:\Users\creal\miniconda3\envs\artifact\python.exe";
        [SerializeField] private string workingDirectory = @"C:\capstone\Shadow-to-3D-Generator";
        [SerializeField] private string scriptName = @"python\MediaPipeTracking.py";
        [SerializeField] private string scriptArguments = "--camera 1 --fallback-cameras 0 --width 640 --height 360 --fps 30 --camera-buffer-size 1 --camera-auto-exposure 0.75 --camera-brightness 180 --camera-gain 80 --camera-contrast 110 --allow-black-frames --frame-gain 2 --frame-brightness-offset 45 --preview";

        private Process launchedProcess;

        private void Start()
        {
            if (launchOnStart)
            {
                Launch();
            }
        }

        private void OnDisable()
        {
            if (stopProcessOnDisable)
            {
                StopProcess();
            }
        }

        private void OnDestroy()
        {
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

            CameraPythonProcessCleanup.KillStaleCameraProcesses(ProcessLabel, workingDirectory);

            string command =
                $"$Host.UI.RawUI.WindowTitle = {QuotePowerShellArgument(ProcessLabel)}; " +
                $"Set-Location -LiteralPath {QuotePowerShellArgument(workingDirectory)}; " +
                $"& {QuotePowerShellArgument(pythonExecutablePath)} {QuotePowerShellArgument(scriptPath)}";

            if (!string.IsNullOrWhiteSpace(scriptArguments))
            {
                command += $" {scriptArguments}";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command {EscapeWindowsArgument(command)}",
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            launchedProcess = new Process { StartInfo = startInfo };
            try
            {
                launchedProcess.Start();
                StartCoroutine(TerminalWindowPlacement.MoveProcessWindowToTerminalDisplayRoutine(
                    launchedProcess,
                    ProcessLabel));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{ProcessLabel}: terminal launch failed: {exception.Message}");
                launchedProcess.Dispose();
                launchedProcess = null;
            }
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

    internal static class CameraPythonProcessCleanup
    {
        private static readonly string[] CameraScriptNames =
        {
            "MediaPipeTracking.py",
            "ArucoTracking.py",
            "Mission5ShadowAreaTracking.py",
            "ShadowMesh.py"
        };

        public static void KillStaleCameraProcesses(string processLabel, string workingDirectory)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return;
            }

            string root;
            try
            {
                root = Path.GetFullPath(workingDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                Debug.LogWarning($"{processLabel}: camera process cleanup skipped: {exception.Message}");
                return;
            }

            string command =
                "$ErrorActionPreference = 'SilentlyContinue'; " +
                $"$root = {QuotePowerShellArgument(root)}; " +
                "$current = $PID; " +
                "Get-CimInstance Win32_Process | Where-Object { " +
                "$_.ProcessId -ne $current -and $_.CommandLine -and " +
                "$_.CommandLine -like ('*' + $root + '*') -and " +
                BuildCameraScriptFilter() +
                " } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command {EscapeWindowsArgument(command)}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using Process cleanupProcess = Process.Start(startInfo);
                cleanupProcess?.WaitForExit(2000);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                Debug.LogWarning($"{processLabel}: camera process cleanup failed: {exception.Message}");
            }
#endif
        }

        private static string BuildCameraScriptFilter()
        {
            string filter = "(";
            for (int i = 0; i < CameraScriptNames.Length; i++)
            {
                if (i > 0)
                {
                    filter += " -or ";
                }

                filter += $"$_.CommandLine -like '*{CameraScriptNames[i]}*'";
            }

            return filter + ")";
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
