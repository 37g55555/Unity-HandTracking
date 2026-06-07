import sys


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
            monitors.append((rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top))
        return True

    user32.EnumDisplayMonitors(0, 0, monitor_enum_proc(callback), 0)
    if not monitors:
        return None

    index = len(monitors) - 1
    return monitors[index]


def configure_preview_window(cv2_module, window_name):
    bounds = get_display_bounds()
    if bounds is None:
        return

    x, y, width, height = bounds
    cv2_module.moveWindow(window_name, x, y)
    cv2_module.resizeWindow(window_name, width, height)
    cv2_module.setWindowProperty(
        window_name,
        cv2_module.WND_PROP_FULLSCREEN,
        cv2_module.WINDOW_FULLSCREEN)
