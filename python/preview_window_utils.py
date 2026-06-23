import sys

TARGET_MONITOR_NUMBER = 2
FALLBACK_TARGET_MONITOR_INDEX = 0
WINDOWED_PREVIEW_WIDTH = 1280
WINDOWED_PREVIEW_HEIGHT = 720
WINDOWED_PREVIEW_OFFSET_X = 40
WINDOWED_PREVIEW_OFFSET_Y = 40
SW_SHOWNOACTIVATE = 4
SWP_NOSIZE = 0x0001
SWP_NOMOVE = 0x0002
SWP_NOZORDER = 0x0004
SWP_NOACTIVATE = 0x0010


def get_display_bounds():
    if not sys.platform.startswith("win"):
        return None

    try:
        import ctypes
    except ImportError:
        return None

    class Rect(ctypes.Structure):
        _fields_ = [
            ("left", ctypes.c_long),
            ("top", ctypes.c_long),
            ("right", ctypes.c_long),
            ("bottom", ctypes.c_long),
        ]

    class MonitorInfo(ctypes.Structure):
        _fields_ = [
            ("cbSize", ctypes.c_ulong),
            ("rcMonitor", Rect),
            ("rcWork", Rect),
            ("dwFlags", ctypes.c_ulong),
            ("szDevice", ctypes.c_wchar * 32),
        ]

    user32 = ctypes.windll.user32
    monitors = []
    monitor_enum_proc = ctypes.WINFUNCTYPE(
        ctypes.c_bool,
        ctypes.c_void_p,
        ctypes.c_void_p,
        ctypes.POINTER(Rect),
        ctypes.c_void_p,
    )

    def callback(hmonitor, _hdc, _rect, _data):
        info = MonitorInfo()
        info.cbSize = ctypes.sizeof(MonitorInfo)
        if user32.GetMonitorInfoW(hmonitor, ctypes.byref(info)):
            rect = info.rcMonitor
            device_name = info.szDevice or ""
            display_number = -1
            digits = ""
            for character in reversed(device_name):
                if not character.isdigit():
                    break
                digits = character + digits
            if digits:
                display_number = int(digits)

            monitors.append({
                "display_number": display_number,
                "bounds": (rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top),
            })
        return True

    user32.EnumDisplayMonitors(0, 0, monitor_enum_proc(callback), 0)
    if not monitors:
        return None

    monitors.sort(key=lambda monitor: (monitor["bounds"][0], monitor["bounds"][1]))

    for monitor in monitors:
        if monitor["display_number"] == TARGET_MONITOR_NUMBER:
            return monitor["bounds"]

    if len(monitors) > FALLBACK_TARGET_MONITOR_INDEX:
        return monitors[FALLBACK_TARGET_MONITOR_INDEX]["bounds"]

    return None


def get_foreground_window():
    if not sys.platform.startswith("win"):
        return None

    try:
        import ctypes
        return ctypes.windll.user32.GetForegroundWindow()
    except (AttributeError, OSError):
        return None


def restore_foreground_window(window_handle):
    if not sys.platform.startswith("win") or not window_handle:
        return

    try:
        import ctypes
        user32 = ctypes.windll.user32
        if user32.IsWindow(window_handle):
            user32.SetForegroundWindow(window_handle)
    except (AttributeError, OSError):
        return


def keep_preview_window_no_activate(window_name, restore_window=None):
    if not sys.platform.startswith("win"):
        return

    try:
        import ctypes
    except ImportError:
        return

    user32 = ctypes.windll.user32
    window_handle = user32.FindWindowW(None, window_name)
    if window_handle:
        user32.ShowWindow(window_handle, SW_SHOWNOACTIVATE)
        user32.SetWindowPos(
            window_handle,
            0,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE,
        )

    restore_foreground_window(restore_window)


def configure_preview_window(cv2_module, window_name, restore_focus_window=None):
    bounds = get_display_bounds()
    if bounds is None:
        keep_preview_window_no_activate(window_name, restore_focus_window)
        return

    x, y, display_width, display_height = bounds
    window_width = min(WINDOWED_PREVIEW_WIDTH, max(320, display_width - (WINDOWED_PREVIEW_OFFSET_X * 2)))
    window_height = min(WINDOWED_PREVIEW_HEIGHT, max(240, display_height - (WINDOWED_PREVIEW_OFFSET_Y * 2)))
    window_x = x + min(WINDOWED_PREVIEW_OFFSET_X, max(0, display_width - window_width))
    window_y = y + min(WINDOWED_PREVIEW_OFFSET_Y, max(0, display_height - window_height))

    cv2_module.setWindowProperty(
        window_name,
        cv2_module.WND_PROP_FULLSCREEN,
        cv2_module.WINDOW_NORMAL)
    cv2_module.moveWindow(window_name, window_x, window_y)
    cv2_module.resizeWindow(window_name, window_width, window_height)
    keep_preview_window_no_activate(window_name, restore_focus_window)
