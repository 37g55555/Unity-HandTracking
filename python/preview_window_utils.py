import sys

TARGET_WINDOWS_DISPLAY_NUMBER = 1
TARGET_MONITOR_INDEX = 0
WINDOWED_PREVIEW_WIDTH = 1280
WINDOWED_PREVIEW_HEIGHT = 720
WINDOWED_PREVIEW_OFFSET_X = 40
WINDOWED_PREVIEW_OFFSET_Y = 40


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
            monitors.append({
                "bounds": (rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top),
                "display_number": parse_display_number(info.szDevice),
            })
        return True

    user32.EnumDisplayMonitors(0, 0, monitor_enum_proc(callback), 0)
    if not monitors:
        return None

    for monitor in monitors:
        if monitor["display_number"] == TARGET_WINDOWS_DISPLAY_NUMBER:
            return monitor["bounds"]

    if len(monitors) > TARGET_MONITOR_INDEX:
        return monitors[TARGET_MONITOR_INDEX]["bounds"]

    return None


def parse_display_number(device_name):
    if not device_name:
        return None

    digits = []
    for character in reversed(device_name):
        if not character.isdigit():
            break

        digits.append(character)

    if not digits:
        return None

    return int("".join(reversed(digits)))


def configure_preview_window(cv2_module, window_name):
    bounds = get_display_bounds()
    if bounds is None:
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
