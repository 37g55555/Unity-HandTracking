using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace ShadowPrototype
{
    internal sealed class DisplayStartupRouter : MonoBehaviour
    {
        private const float MoveRetrySeconds = 5.0f;
        private const float MoveRetryIntervalSeconds = 0.1f;

        private static DisplayStartupRouter instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            DisplayRoutingSettings.ActivateConfiguredUnityDisplays();
            StoreMainMonitorPreferenceForNextLaunch();

            if (instance != null)
            {
                return;
            }

            GameObject routerObject = new GameObject("DisplayStartupRouter");
            DontDestroyOnLoad(routerObject);
            instance = routerObject.AddComponent<DisplayStartupRouter>();
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

        private IEnumerator Start()
        {
            DisplayRoutingSettings.ActivateConfiguredUnityDisplays();

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            yield return MoveUnityWindowToMainDisplayRoutine();
#else
            yield break;
#endif
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                DisplayRoutingSettings.ActivateConfiguredUnityDisplays();
            }
        }

        private static void StoreMainMonitorPreferenceForNextLaunch()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            int monitorNumber = Mathf.Max(1, DisplayRoutingSettings.MainWindowsDisplayNumber);
            PlayerPrefs.SetInt("UnitySelectMonitor", monitorNumber);
            PlayerPrefs.Save();
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private IEnumerator MoveUnityWindowToMainDisplayRoutine()
        {
            if (!WindowsDisplayUtility.TryGetMonitorBoundsByDisplayNumber(
                    DisplayRoutingSettings.MainWindowsDisplayNumber,
                    useWorkArea: false,
                    out RectInt mainBounds,
                    out string mainDescription))
            {
                UnityEngine.Debug.LogWarning(
                    $"DisplayStartupRouter: Windows display {DisplayRoutingSettings.MainWindowsDisplayNumber} was not found.");
                yield break;
            }

            if (!WindowsDisplayUtility.TryGetMonitorBoundsByDisplayNumber(
                    DisplayRoutingSettings.HologramWindowsDisplayNumber,
                    useWorkArea: false,
                    out RectInt hologramBounds,
                    out string hologramDescription))
            {
                UnityEngine.Debug.LogWarning(
                    $"DisplayStartupRouter: Windows display {DisplayRoutingSettings.HologramWindowsDisplayNumber} was not found.");
                yield break;
            }

            if (mainBounds.width <= 0 || mainBounds.height <= 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"DisplayStartupRouter: invalid target display bounds for {mainDescription}: {mainBounds}.");
                yield break;
            }

            if (hologramBounds.width <= 0 || hologramBounds.height <= 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"DisplayStartupRouter: invalid hologram display bounds for {hologramDescription}: {hologramBounds}.");
                yield break;
            }

            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.fullScreen = false;
            Screen.SetResolution(mainBounds.width, mainBounds.height, false);
            yield return null;

            float deadline = Time.realtimeSinceStartup + MoveRetrySeconds;
            bool mainMoved = false;
            bool hologramMoved = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                DisplayRoutingSettings.ActivateConfiguredUnityDisplays();

                if (TryGetCurrentProcessWindowInfo(out int processId, out IntPtr mainWindowHandle))
                {
                    bool mainMovedThisFrame = WindowsDisplayUtility.MoveBorderlessWindowToBounds(
                        mainWindowHandle,
                        mainBounds);
                    bool hologramMovedThisFrame = TryMoveHologramWindow(
                        processId,
                        mainWindowHandle,
                        hologramBounds);

                    mainMoved = mainMoved || mainMovedThisFrame;
                    hologramMoved = hologramMoved || hologramMovedThisFrame;

                    if (mainMoved && hologramMoved)
                    {
                        yield return null;
                        ForceMoveMainWindow(mainBounds);
                        UnityEngine.Debug.Log(
                            $"DisplayStartupRouter: Unity main window moved to {mainDescription} " +
                            $"and hologram display moved to {hologramDescription}.");
                        yield break;
                    }
                }

                yield return new WaitForSecondsRealtime(MoveRetryIntervalSeconds);
            }

            if (!mainMoved)
            {
                UnityEngine.Debug.LogWarning(
                    $"DisplayStartupRouter: Unity main window was not moved to Windows display " +
                    $"{DisplayRoutingSettings.MainWindowsDisplayNumber}.");
            }

            if (!hologramMoved)
            {
                UnityEngine.Debug.LogWarning(
                    $"DisplayStartupRouter: Unity hologram display window was not moved to Windows display " +
                    $"{DisplayRoutingSettings.HologramWindowsDisplayNumber}.");
            }

            if (mainMoved)
            {
                yield return null;
                ForceMoveMainWindow(mainBounds);
            }
        }

        private static bool TryGetCurrentProcessWindowInfo(out int processId, out IntPtr mainWindowHandle)
        {
            processId = 0;
            mainWindowHandle = IntPtr.Zero;

            Process currentProcess = Process.GetCurrentProcess();
            try
            {
                currentProcess.Refresh();
                processId = currentProcess.Id;
                mainWindowHandle = currentProcess.MainWindowHandle;
                return processId > 0 && mainWindowHandle != IntPtr.Zero;
            }
            finally
            {
                currentProcess.Dispose();
            }
        }

        private static bool TryMoveHologramWindow(int processId, IntPtr mainWindowHandle, RectInt targetBounds)
        {
            IntPtr hologramWindowHandle = IntPtr.Zero;
            int bestArea = 0;
            foreach (IntPtr windowHandle in WindowsDisplayUtility.GetVisibleTopLevelWindowsForProcess(processId))
            {
                if (windowHandle == mainWindowHandle ||
                    !WindowsDisplayUtility.TryGetWindowBounds(windowHandle, out RectInt windowBounds))
                {
                    continue;
                }

                int area = windowBounds.width * windowBounds.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    hologramWindowHandle = windowHandle;
                }
            }

            return hologramWindowHandle != IntPtr.Zero &&
                WindowsDisplayUtility.MoveWindowToBounds(hologramWindowHandle, targetBounds, true);
        }

        private static void ForceMoveMainWindow(RectInt targetBounds)
        {
            if (TryGetCurrentProcessWindowInfo(out _, out IntPtr mainWindowHandle))
            {
                WindowsDisplayUtility.MoveBorderlessWindowToBounds(mainWindowHandle, targetBounds);
            }
        }
#endif
    }
}
