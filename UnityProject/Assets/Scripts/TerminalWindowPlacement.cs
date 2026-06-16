using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace ShadowPrototype
{
    internal static class TerminalWindowPlacement
    {
        private static readonly Vector2Int TerminalWindowOffset = new Vector2Int(40, 40);
        private static readonly Vector2Int TerminalWindowSize = new Vector2Int(980, 620);
        private static readonly Vector2Int TerminalWindowCascadeOffset = new Vector2Int(32, 32);

        public static IEnumerator MoveProcessWindowToTerminalDisplayRoutine(
            Process process,
            string processLabel,
            int launchIndex = 0)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IntPtr windowHandle = IntPtr.Zero;
            float deadline = Time.realtimeSinceStartup + 3.0f;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (process == null)
                {
                    yield break;
                }

                try
                {
                    if (process.HasExited)
                    {
                        yield break;
                    }

                    process.Refresh();
                    windowHandle = process.MainWindowHandle;
                }
                catch (Exception exception) when (
                    exception is ObjectDisposedException ||
                    exception is InvalidOperationException ||
                    exception is System.ComponentModel.Win32Exception)
                {
                    yield break;
                }

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
                UnityEngine.Debug.LogWarning($"{processLabel}: terminal window handle was not found; window position was not changed.");
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
                UnityEngine.Debug.LogWarning($"{processLabel}: target terminal display was not found; window position was not changed.");
                yield break;
            }

            WindowsDisplayUtility.MoveWindowToBounds(windowHandle, new RectInt(x, y, width, height), true);
            UnityEngine.Debug.Log($"{processLabel}: terminal window moved to {targetDescription} at ({x}, {y}) without activation.");
#else
            yield break;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static bool TryGetTerminalWindowPlacement(
            int launchIndex,
            out int x,
            out int y,
            out int width,
            out int height,
            out string targetDescription)
        {
            if (!WindowsDisplayUtility.TryGetMonitorBoundsByDisplayNumber(
                    DisplayRoutingSettings.TerminalWindowsDisplayNumber,
                    useWorkArea: true,
                    out RectInt displayBounds,
                    out targetDescription))
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
