using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace ShadowPrototype
{
    internal static class TerminalWindowRouter
    {
        private static readonly Vector2Int WindowOffset = new Vector2Int(40, 40);
        private static readonly Vector2Int WindowSize = new Vector2Int(980, 620);
        private static readonly Vector2Int CascadeOffset = new Vector2Int(32, 32);

        public static IEnumerator MoveToConfiguredDisplayRoutine(Process process, string processLabel, int launchIndex = 0)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (process == null)
            {
                yield break;
            }

            for (int attempt = 0; attempt < 40; attempt++)
            {
                System.IntPtr windowHandle = System.IntPtr.Zero;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Refresh();
                        windowHandle = process.MainWindowHandle;
                    }
                }
                catch (System.Exception exception) when (exception is System.InvalidOperationException || exception is System.ComponentModel.Win32Exception)
                {
                    yield break;
                }

                if (windowHandle == System.IntPtr.Zero)
                {
                    windowHandle = WindowsDisplayUtility.FindWindowByTitle(processLabel);
                }

                if (windowHandle != System.IntPtr.Zero &&
                    TryGetTerminalWindowPlacement(launchIndex, out RectInt placement, out string targetDescription))
                {
                    WindowsDisplayUtility.MoveWindowToBounds(windowHandle, placement, true);
                    UnityEngine.Debug.Log($"{processLabel}: terminal window moved to {targetDescription}.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.05f);
            }
#else
            yield break;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static bool TryGetTerminalWindowPlacement(int launchIndex, out RectInt placement, out string targetDescription)
        {
            if (!WindowsDisplayUtility.TryGetMonitorBoundsByWindowsDisplayNumber(
                    DisplayRoutingSettings.TerminalWindowsDisplayNumber,
                    useWorkArea: true,
                    out RectInt displayBounds,
                    out targetDescription))
            {
                placement = default;
                return false;
            }

            int width = Mathf.Clamp(WindowSize.x, 320, displayBounds.width);
            int height = Mathf.Clamp(WindowSize.y, 240, displayBounds.height);
            int cascadeX = CascadeOffset.x * (launchIndex % 6);
            int cascadeY = CascadeOffset.y * (launchIndex % 6);
            int maxX = Mathf.Max(0, displayBounds.width - width);
            int maxY = Mathf.Max(0, displayBounds.height - height);
            int x = displayBounds.x + Mathf.Clamp(WindowOffset.x + cascadeX, 0, maxX);
            int y = displayBounds.y + Mathf.Clamp(WindowOffset.y + cascadeY, 0, maxY);
            placement = new RectInt(x, y, width, height);
            return true;
        }
#endif
    }
}
