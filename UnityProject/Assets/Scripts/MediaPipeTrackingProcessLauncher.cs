using System;
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
        [SerializeField] private string pythonExecutablePath = @"D:\anaconda3\envs\artifact\python.exe";
        [SerializeField] private string workingDirectory = @"D:\Unity-HandTracking";
        [SerializeField] private string scriptName = @"python\MediaPipeTracking.py";
        [SerializeField] private string scriptArguments = "--camera 0";

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
