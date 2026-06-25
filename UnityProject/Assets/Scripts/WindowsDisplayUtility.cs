using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ShadowPrototype
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    internal readonly struct WindowsDisplayArea
    {
        public WindowsDisplayArea(int displayNumber, RectInt bounds, RectInt workArea, string description)
        {
            DisplayNumber = displayNumber;
            Bounds = bounds;
            WorkArea = workArea;
            Description = description;
        }

        public int DisplayNumber { get; }
        public RectInt Bounds { get; }
        public RectInt WorkArea { get; }
        public string Description { get; }
    }

    internal static class WindowsDisplayUtility
    {
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool repaint);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private delegate bool WindowEnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowEnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private const int ShowWindowRestore = 9;
        private const uint SetWindowPosNoZOrder = 0x0004;
        private const uint SetWindowPosShowWindow = 0x0040;

        public static IntPtr FindWindowByTitle(string title)
        {
            return string.IsNullOrWhiteSpace(title) ? IntPtr.Zero : FindWindow(null, title);
        }

        public static bool MoveWindowToBounds(IntPtr windowHandle, RectInt bounds, bool repaint)
        {
            if (windowHandle == IntPtr.Zero || bounds.width <= 0 || bounds.height <= 0)
            {
                return false;
            }

            ShowWindow(windowHandle, ShowWindowRestore);
            if (SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    bounds.x,
                    bounds.y,
                    bounds.width,
                    bounds.height,
                    SetWindowPosNoZOrder | SetWindowPosShowWindow))
            {
                return true;
            }

            return MoveWindow(windowHandle, bounds.x, bounds.y, bounds.width, bounds.height, repaint);
        }

        public static bool TryGetLargestVisibleWindowForCurrentProcess(out IntPtr windowHandle)
        {
            int currentProcessId = Process.GetCurrentProcess().Id;
            IntPtr bestWindowHandle = IntPtr.Zero;
            int bestWindowArea = 0;

            EnumWindows(
                (candidateHandle, _) =>
                {
                    if (!IsWindowVisible(candidateHandle))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(candidateHandle, out uint processId);
                    if (processId != currentProcessId)
                    {
                        return true;
                    }

                    if (!GetWindowRect(candidateHandle, out NativeRect rect))
                    {
                        return true;
                    }

                    int width = rect.right - rect.left;
                    int height = rect.bottom - rect.top;
                    int area = width * height;
                    if (width >= 320 && height >= 240 && area > bestWindowArea)
                    {
                        bestWindowHandle = candidateHandle;
                        bestWindowArea = area;
                    }

                    return true;
                },
                IntPtr.Zero);

            windowHandle = bestWindowHandle;
            return windowHandle != IntPtr.Zero;
        }

        public static bool TryGetSecondaryVisibleWindowForCurrentProcess(IntPtr mainWindowHandle, out IntPtr windowHandle)
        {
            int currentProcessId = Process.GetCurrentProcess().Id;
            IntPtr bestWindowHandle = IntPtr.Zero;
            int bestWindowArea = 0;

            EnumWindows(
                (candidateHandle, _) =>
                {
                    if (candidateHandle == mainWindowHandle || !IsWindowVisible(candidateHandle))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(candidateHandle, out uint processId);
                    if (processId != currentProcessId)
                    {
                        return true;
                    }

                    if (!GetWindowRect(candidateHandle, out NativeRect rect))
                    {
                        return true;
                    }

                    int width = rect.right - rect.left;
                    int height = rect.bottom - rect.top;
                    int area = width * height;
                    if (width < 320 || height < 240)
                    {
                        return true;
                    }

                    string title = GetWindowTitle(candidateHandle);
                    if (title.IndexOf("Unity Secondary Display", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bestWindowHandle = candidateHandle;
                        bestWindowArea = int.MaxValue;
                        return false;
                    }

                    if (area > bestWindowArea)
                    {
                        bestWindowHandle = candidateHandle;
                        bestWindowArea = area;
                    }

                    return true;
                },
                IntPtr.Zero);

            windowHandle = bestWindowHandle;
            return windowHandle != IntPtr.Zero;
        }

        public static bool TryGetMonitorBoundsByPositionIndex(
            int monitorIndex,
            bool useWorkArea,
            out RectInt bounds,
            out string description)
        {
            List<WindowsDisplayArea> displays = GetDisplayAreas();
            if (displays.Count == 0)
            {
                bounds = default;
                description = "no monitor";
                return false;
            }

            SortDisplayAreasByPosition(displays);
            int safeIndex = Mathf.Clamp(monitorIndex, 0, displays.Count - 1);
            WindowsDisplayArea display = displays[safeIndex];
            bounds = useWorkArea ? display.WorkArea : display.Bounds;
            description = $"{display.Description}, position {safeIndex + 1}";
            return true;
        }

        public static bool TryGetMonitorBoundsByWindowsDisplayNumber(
            int displayNumber,
            bool useWorkArea,
            out RectInt bounds,
            out string description)
        {
            List<WindowsDisplayArea> displays = GetDisplayAreas();
            for (int index = 0; index < displays.Count; index++)
            {
                WindowsDisplayArea display = displays[index];
                if (display.DisplayNumber != displayNumber)
                {
                    continue;
                }

                bounds = useWorkArea ? display.WorkArea : display.Bounds;
                description = display.Description;
                return true;
            }

            bounds = default;
            description = $"Windows display {displayNumber}";
            return false;
        }

        private static List<WindowsDisplayArea> GetDisplayAreas()
        {
            var displays = new List<WindowsDisplayArea>();
            EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (monitor, _, _, _) =>
                {
                    var info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        int displayNumber = ParseDisplayNumber(info.szDevice);
                        displays.Add(new WindowsDisplayArea(
                            displayNumber,
                            ToRectInt(info.rcMonitor),
                            ToRectInt(info.rcWork),
                            displayNumber > 0
                                ? $"Windows display {displayNumber}"
                                : $"monitor index {displays.Count + 1}"));
                    }

                    return true;
                },
                IntPtr.Zero);
            return displays;
        }

        private static void SortDisplayAreasByPosition(List<WindowsDisplayArea> displays)
        {
            displays.Sort((left, right) =>
            {
                int xCompare = left.Bounds.x.CompareTo(right.Bounds.x);
                return xCompare != 0 ? xCompare : left.Bounds.y.CompareTo(right.Bounds.y);
            });
        }

        private static RectInt ToRectInt(NativeRect rect)
        {
            return new RectInt(
                rect.left,
                rect.top,
                rect.right - rect.left,
                rect.bottom - rect.top);
        }

        private static string GetWindowTitle(IntPtr windowHandle)
        {
            int titleLength = GetWindowTextLength(windowHandle);
            if (titleLength <= 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(titleLength + 1);
            GetWindowText(windowHandle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static int ParseDisplayNumber(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return -1;
            }

            int multiplier = 1;
            int value = 0;
            bool foundDigit = false;
            for (int index = deviceName.Length - 1; index >= 0; index--)
            {
                char character = deviceName[index];
                if (!char.IsDigit(character))
                {
                    break;
                }

                foundDigit = true;
                value += (character - '0') * multiplier;
                multiplier *= 10;
            }

            return foundDigit ? value : -1;
        }
    }
#endif
}
