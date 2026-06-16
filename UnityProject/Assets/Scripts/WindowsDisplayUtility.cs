using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        private delegate bool WindowEnumProc(IntPtr hWnd, IntPtr lParam);
        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private const int GwlStyle = -16;
        private const int WsCaption = 0x00C00000;
        private const int WsThickFrame = 0x00040000;
        private const int WsMinimize = 0x20000000;
        private const int WsMaximize = 0x01000000;
        private const int WsSysMenu = 0x00080000;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowEnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

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

            return MoveWindow(windowHandle, bounds.x, bounds.y, bounds.width, bounds.height, repaint);
        }

        public static bool MoveBorderlessWindowToBounds(IntPtr windowHandle, RectInt bounds)
        {
            if (windowHandle == IntPtr.Zero || bounds.width <= 0 || bounds.height <= 0)
            {
                return false;
            }

            MakeWindowBorderless(windowHandle);
            return SetWindowPos(
                windowHandle,
                HwndTop,
                bounds.x,
                bounds.y,
                bounds.width,
                bounds.height,
                SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
        }

        private static void MakeWindowBorderless(IntPtr windowHandle)
        {
            int style = GetWindowLong(windowHandle, GwlStyle);
            if (style == 0)
            {
                return;
            }

            int borderlessStyle = style & ~(WsCaption | WsThickFrame | WsMinimize | WsMaximize | WsSysMenu);
            if (borderlessStyle != style)
            {
                SetWindowLong(windowHandle, GwlStyle, borderlessStyle);
                SetWindowPos(
                    windowHandle,
                    HwndTop,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
            }
        }

        public static List<IntPtr> GetVisibleTopLevelWindowsForProcess(int processId)
        {
            var windows = new List<IntPtr>();
            if (processId <= 0)
            {
                return windows;
            }

            EnumWindows(
                (windowHandle, _) =>
                {
                    GetWindowThreadProcessId(windowHandle, out int windowProcessId);
                    if (windowProcessId == processId &&
                        IsWindowVisible(windowHandle) &&
                        TryGetWindowBounds(windowHandle, out RectInt windowBounds) &&
                        windowBounds.width > 0 &&
                        windowBounds.height > 0)
                    {
                        windows.Add(windowHandle);
                    }

                    return true;
                },
                IntPtr.Zero);
            return windows;
        }

        public static bool TryGetWindowBounds(IntPtr windowHandle, out RectInt bounds)
        {
            if (windowHandle == IntPtr.Zero || !GetWindowRect(windowHandle, out NativeRect rect))
            {
                bounds = default;
                return false;
            }

            bounds = ToRectInt(rect);
            return true;
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

        public static bool TryGetMonitorBoundsByDisplayNumber(
            int displayNumber,
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

            for (int index = 0; index < displays.Count; index++)
            {
                WindowsDisplayArea display = displays[index];
                if (display.DisplayNumber == displayNumber)
                {
                    bounds = useWorkArea ? display.WorkArea : display.Bounds;
                    description = display.Description;
                    return true;
                }
            }

            SortDisplayAreasByPosition(displays);
            int fallbackIndex = Mathf.Clamp(displayNumber - 1, 0, displays.Count - 1);
            WindowsDisplayArea fallbackDisplay = displays[fallbackIndex];
            bounds = useWorkArea ? fallbackDisplay.WorkArea : fallbackDisplay.Bounds;
            description = $"{fallbackDisplay.Description}, position {fallbackIndex + 1} fallback for Windows display {displayNumber}";
            return true;
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
