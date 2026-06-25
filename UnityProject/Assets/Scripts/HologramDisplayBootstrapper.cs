using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShadowPrototype
{
    public sealed class HologramDisplayBootstrapper : MonoBehaviour
    {
        private const int ActivationRetryCount = 60;
        private const float ActivationRetryIntervalSeconds = 0.1f;
        private const int MainWindowPostActivationRouteCount = 40;
        private const float SecondaryWindowRouteIntervalSeconds = 0.25f;
        private static bool created;

        private Camera blankDisplayCamera;

#if UNITY_STANDALONE_WIN
        private IntPtr mainUnityWindowHandle = IntPtr.Zero;
        private IntPtr secondaryUnityWindowHandle = IntPtr.Zero;
        private RectInt mainUnityWindowBounds;
        private RectInt secondaryUnityWindowBounds;
        private string mainUnityWindowDisplayDescription = string.Empty;
        private string secondaryUnityWindowDisplayDescription = string.Empty;
        private bool secondaryUnityWindowRouteLogged;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_STANDALONE
            if (created)
            {
                return;
            }

            created = true;
            var bootstrapObject = new GameObject(nameof(HologramDisplayBootstrapper));
            DontDestroyOnLoad(bootstrapObject);
            bootstrapObject.AddComponent<HologramDisplayBootstrapper>();
#endif
        }

        private void Start()
        {
#if UNITY_STANDALONE
            StartCoroutine(PrepareDisplayWindowsRoutine());
#endif
        }

#if UNITY_STANDALONE
        private IEnumerator PrepareDisplayWindowsRoutine()
        {
#if UNITY_STANDALONE_WIN
            yield return CaptureAndRouteMainWindowToBeamDisplayRoutine();
#endif
            yield return ActivateHologramDisplayRoutine();
#if UNITY_STANDALONE_WIN
            StartCoroutine(EnforceSecondaryWindowToHologramDisplayRoutine());
            yield return RouteCapturedMainWindowToBeamDisplayRoutine();
            yield return EnforceMainWindowToBeamDisplayRoutine();
#endif
        }

#if UNITY_STANDALONE_WIN
        private IEnumerator CaptureAndRouteMainWindowToBeamDisplayRoutine()
        {
            if (!WindowsDisplayUtility.TryGetMonitorBoundsByWindowsDisplayNumber(
                    DisplayRoutingSettings.MainUnityWindowsDisplayNumber,
                    useWorkArea: false,
                    out mainUnityWindowBounds,
                    out mainUnityWindowDisplayDescription))
            {
                Debug.LogWarning($"HologramDisplayBootstrapper: Windows display {DisplayRoutingSettings.MainUnityWindowsDisplayNumber} was not found.");
                yield break;
            }

            bool resolutionApplied = false;
            for (int attempt = 0; attempt < ActivationRetryCount; attempt++)
            {
                if (mainUnityWindowHandle == IntPtr.Zero)
                {
                    using (Process process = Process.GetCurrentProcess())
                    {
                        process.Refresh();
                        mainUnityWindowHandle = process.MainWindowHandle;
                    }

                    if (mainUnityWindowHandle == IntPtr.Zero)
                    {
                        WindowsDisplayUtility.TryGetLargestVisibleWindowForCurrentProcess(out mainUnityWindowHandle);
                    }
                }

                if (mainUnityWindowHandle != IntPtr.Zero)
                {
                    if (!resolutionApplied)
                    {
                        Screen.fullScreenMode = FullScreenMode.Windowed;
                        Screen.SetResolution(mainUnityWindowBounds.width, mainUnityWindowBounds.height, false);
                        resolutionApplied = true;
                        yield return null;
                    }

                    if (WindowsDisplayUtility.MoveWindowToBounds(mainUnityWindowHandle, mainUnityWindowBounds, true))
                    {
                        Debug.Log($"HologramDisplayBootstrapper: routed main Unity window to {mainUnityWindowDisplayDescription}.");
                        yield break;
                    }
                }

                yield return new WaitForSecondsRealtime(ActivationRetryIntervalSeconds);
            }

            Debug.LogWarning($"HologramDisplayBootstrapper: main Unity window could not be routed to {mainUnityWindowDisplayDescription}.");
        }

        private IEnumerator RouteCapturedMainWindowToBeamDisplayRoutine()
        {
            if (mainUnityWindowHandle == IntPtr.Zero || mainUnityWindowBounds.width <= 0 || mainUnityWindowBounds.height <= 0)
            {
                yield break;
            }

            yield return null;
            yield return null;

            if (WindowsDisplayUtility.MoveWindowToBounds(mainUnityWindowHandle, mainUnityWindowBounds, true))
            {
                Debug.Log($"HologramDisplayBootstrapper: restored captured main Unity window to {mainUnityWindowDisplayDescription}.");
            }
            else
            {
                Debug.LogWarning($"HologramDisplayBootstrapper: captured main Unity window could not be restored to {mainUnityWindowDisplayDescription}.");
            }
        }

        private IEnumerator EnforceMainWindowToBeamDisplayRoutine()
        {
            if (mainUnityWindowHandle == IntPtr.Zero ||
                mainUnityWindowBounds.width <= 0 ||
                mainUnityWindowBounds.height <= 0)
            {
                yield break;
            }

            for (int attempt = 0; attempt < MainWindowPostActivationRouteCount; attempt++)
            {
                Screen.fullScreenMode = FullScreenMode.Windowed;
                Screen.SetResolution(mainUnityWindowBounds.width, mainUnityWindowBounds.height, false);

                if (WindowsDisplayUtility.MoveWindowToBounds(mainUnityWindowHandle, mainUnityWindowBounds, true))
                {
                    Debug.Log($"HologramDisplayBootstrapper: enforced main Unity window on {mainUnityWindowDisplayDescription}.");
                }

                yield return new WaitForSecondsRealtime(ActivationRetryIntervalSeconds);
            }
        }

        private IEnumerator EnforceSecondaryWindowToHologramDisplayRoutine()
        {
            if (!WindowsDisplayUtility.TryGetMonitorBoundsByWindowsDisplayNumber(
                    DisplayRoutingSettings.HologramUnityWindowsDisplayNumber,
                    useWorkArea: false,
                    out secondaryUnityWindowBounds,
                    out secondaryUnityWindowDisplayDescription))
            {
                Debug.LogWarning($"HologramDisplayBootstrapper: Windows display {DisplayRoutingSettings.HologramUnityWindowsDisplayNumber} was not found for Unity Secondary Display.");
                yield break;
            }

            while (true)
            {
                if (mainUnityWindowHandle != IntPtr.Zero)
                {
                    if (secondaryUnityWindowHandle == IntPtr.Zero ||
                        secondaryUnityWindowHandle == mainUnityWindowHandle)
                    {
                        WindowsDisplayUtility.TryGetSecondaryVisibleWindowForCurrentProcess(
                            mainUnityWindowHandle,
                            out secondaryUnityWindowHandle);
                    }

                    if (secondaryUnityWindowHandle != IntPtr.Zero)
                    {
                        if (WindowsDisplayUtility.MoveWindowToBounds(
                                secondaryUnityWindowHandle,
                                secondaryUnityWindowBounds,
                                true))
                        {
                            if (!secondaryUnityWindowRouteLogged)
                            {
                                Debug.Log($"HologramDisplayBootstrapper: routed Unity Secondary Display to {secondaryUnityWindowDisplayDescription}.");
                                secondaryUnityWindowRouteLogged = true;
                            }
                        }
                        else
                        {
                            secondaryUnityWindowHandle = IntPtr.Zero;
                        }
                    }
                }

                yield return new WaitForSecondsRealtime(SecondaryWindowRouteIntervalSeconds);
            }
        }
#endif

        private IEnumerator ActivateHologramDisplayRoutine()
        {
            for (int attempt = 0; attempt < ActivationRetryCount; attempt++)
            {
                int displayIndex = DisplayRoutingSettings.ResolveUnityDisplayIndex(
                    DisplayRoutingSettings.HologramUnityDisplayIndex);
                if (displayIndex > 0 && displayIndex < Display.displays.Length)
                {
                    DisplayRoutingSettings.ActivateUnityDisplay(displayIndex);
                    EnsureBlankDisplayCamera(displayIndex);
                    Debug.Log($"HologramDisplayBootstrapper: activated Unity display {displayIndex} at startup.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(ActivationRetryIntervalSeconds);
            }

            Debug.LogWarning("HologramDisplayBootstrapper: hologram Unity display was not available at startup.");
        }

        private void EnsureBlankDisplayCamera(int displayIndex)
        {
            if (blankDisplayCamera != null)
            {
                blankDisplayCamera.targetDisplay = displayIndex;
                return;
            }

            var cameraObject = new GameObject("HologramBlankDisplayCamera");
            cameraObject.transform.SetParent(transform, false);

            blankDisplayCamera = cameraObject.AddComponent<Camera>();
            blankDisplayCamera.targetDisplay = displayIndex;
            blankDisplayCamera.clearFlags = CameraClearFlags.SolidColor;
            blankDisplayCamera.backgroundColor = Color.black;
            blankDisplayCamera.cullingMask = 0;
            blankDisplayCamera.depth = -1000.0f;
            blankDisplayCamera.allowHDR = false;
            blankDisplayCamera.allowMSAA = false;
        }
#endif
    }
}
