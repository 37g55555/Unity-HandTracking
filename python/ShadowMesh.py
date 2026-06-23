"""Capture a shadow silhouette and export a 2D mesh for Unity."""

import json
import os
import sys
import argparse
import time

from preview_window_utils import configure_preview_window

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

OUTPUT_DIR = os.path.join("output", "shadowmesh")
BACKGROUND_FILE_NAME = "background.png"
CONTOUR_FILE_NAME = "shadow_contour.png"
MESH_FILE_NAME = "shadow_mesh.obj"
METADATA_FILE_NAME = "shadow_metadata.json"
PREVIEW_WINDOW_NAME = "Shadow Capture"
CONTROL_WINDOW_NAME = "Shadow Capture Controls"
CAMERA_WIDTH = 640
CAMERA_HEIGHT = 360
CAMERA_FPS = 30
CAMERA_BUFFER_SIZE = 1
CAMERA_GAIN = 90.0
CAMERA_BRIGHTNESS = 150.0
CAMERA_CONTRAST = 55.0
CAMERA_EXPOSURE = None
CAMERA_AUTO_EXPOSURE = 0.75
FRAME_ENHANCE_ALPHA = 1.45
FRAME_ENHANCE_BETA = 20.0
FRAME_ENHANCE_GAMMA = 0.75
CONTROL_WINDOW_WIDTH = 520
CONTROL_WINDOW_HEIGHT = 360
CONTROL_WINDOW_OFFSET_X = 1360
CONTROL_WINDOW_OFFSET_Y = 40
EPSILON_RATIO = 0.0015
INTERIOR_SPACING = 7
FOREARM_TRIM_ENABLED = True
CAPTURE_REFERENCE_WIDTH = 640.0
CAPTURE_REFERENCE_HEIGHT = 360.0
SOFT_WHITE_CIRCLE_CENTER_X = 320.0
SOFT_WHITE_CIRCLE_CENTER_Y = 180.0
SOFT_WHITE_CIRCLE_RADIUS = 112.0
FOREARM_TRIM_CENTER_X_RATIO = SOFT_WHITE_CIRCLE_CENTER_X / CAPTURE_REFERENCE_WIDTH
FOREARM_TRIM_CENTER_Y_RATIO = SOFT_WHITE_CIRCLE_CENTER_Y / CAPTURE_REFERENCE_HEIGHT
FOREARM_TRIM_RADIUS_RATIO = SOFT_WHITE_CIRCLE_RADIUS / CAPTURE_REFERENCE_HEIGHT
FOREARM_TRIM_CLOSE_KERNEL = 9
FOREARM_TRIM_MIN_REMAINING_RATIO = 0.05
SHADOW_AUTO_CAPTURE_ENABLED = True
SHADOW_AUTO_CAPTURE_SECONDS = 1.7
SHADOW_STILLNESS_DIFF_THRESHOLD = 22
SHADOW_STILLNESS_MOTION_RATIO = 0.03
SHADOW_PRESENCE_DIFF_THRESHOLD = 20
SHADOW_PRESENCE_MIN_RATIO = 0.06
SHADOW_STILLNESS_SAMPLE_WIDTH = 320
BACKGROUND_AUTO_CAPTURE_SECONDS = 1.0
SHADOW_ARM_DELAY_SECONDS = 2.0
SHADOW_CONFIRM_SECONDS = 0.5
ENTER_KEYS = {10, 13}
QUIT_KEY = ord("q")
CAMERA_NO_FRAME_REOPEN_SECONDS = 1.0
CAMERA_BLACK_FRAME_REOPEN_SECONDS = 1.2
CAMERA_BLACK_FRAME_MEAN_THRESHOLD = 3.0
CAMERA_REOPEN_BACKOFF_SECONDS = 0.35

cv2 = None
np = None
tr = None


def load_mesh_generation_dependencies():
    global cv2, np, tr

    if cv2 is not None and np is not None and tr is not None:
        return

    import cv2 as cv2_module
    import numpy as np_module
    import triangle as triangle_module

    cv2 = cv2_module
    np = np_module
    tr = triangle_module

    try:
        cv2.setUseOptimized(True)
    except cv2.error:
        pass

    try:
        cv2.setNumThreads(1)
    except cv2.error:
        pass


def get_camera_backend_candidates():
    candidates = []

    if sys.platform.startswith("win"):
        if hasattr(cv2, "CAP_DSHOW"):
            candidates.append(("DirectShow", cv2.CAP_DSHOW))
        if hasattr(cv2, "CAP_MSMF"):
            candidates.append(("Media Foundation", cv2.CAP_MSMF))
        candidates.append(("default", None))
        return candidates

    if sys.platform == "darwin" and hasattr(cv2, "CAP_AVFOUNDATION"):
        candidates.append(("AVFoundation", cv2.CAP_AVFOUNDATION))

    candidates.append(("default", None))
    return candidates


def create_capture(camera_id):
    last_capture = None
    for backend_name, backend in get_camera_backend_candidates():
        cap = cv2.VideoCapture(camera_id, backend) if backend is not None else cv2.VideoCapture(camera_id)
        if cap.isOpened():
            print(f"[INFO] Camera {camera_id} opened with {backend_name}.")
            return cap

        cap.release()
        last_capture = cap

    if last_capture is not None:
        last_capture.release()
    return None


def read_valid_frame(cap, timeout=1.0):
    deadline = time.perf_counter() + max(0.0, timeout)
    while time.perf_counter() <= deadline:
        ok, frame = cap.read()
        if ok and frame is not None and frame.size > 0:
            return True, frame
        time.sleep(0.02)

    return False, None


def is_nearly_black_frame(frame, mean_threshold=CAMERA_BLACK_FRAME_MEAN_THRESHOLD):
    if frame is None or frame.size <= 0:
        return False

    return float(frame.mean()) <= float(mean_threshold)


def set_capture_property(cap, property_id, value, label):
    if value is None:
        return

    try:
        requested = float(value)
    except (TypeError, ValueError):
        print(f"[WARN] Camera {label} value is invalid: {value}")
        return

    success = cap.set(property_id, requested)
    actual = cap.get(property_id)
    status = "OK" if success else "WARN"
    print(f"[{status}] Camera {label}: requested {requested:g}, actual {actual:g}")


def apply_camera_sensitivity(cap, camera_tuning):
    if not camera_tuning or not camera_tuning["enabled"]:
        return

    if camera_tuning["auto_exposure"] is not None:
        set_capture_property(
            cap,
            cv2.CAP_PROP_AUTO_EXPOSURE,
            camera_tuning["auto_exposure"],
            "auto exposure"
        )

    if camera_tuning["exposure"] is not None:
        set_capture_property(cap, cv2.CAP_PROP_EXPOSURE, camera_tuning["exposure"], "exposure")

    set_capture_property(cap, cv2.CAP_PROP_GAIN, camera_tuning["gain"], "gain")
    set_capture_property(cap, cv2.CAP_PROP_BRIGHTNESS, camera_tuning["brightness"], "brightness")
    set_capture_property(cap, cv2.CAP_PROP_CONTRAST, camera_tuning["contrast"], "contrast")


def build_camera_tuning_config(
    enabled=True,
    gain=CAMERA_GAIN,
    brightness=CAMERA_BRIGHTNESS,
    contrast=CAMERA_CONTRAST,
    exposure=CAMERA_EXPOSURE,
    auto_exposure=CAMERA_AUTO_EXPOSURE,
):
    return {
        "enabled": bool(enabled),
        "gain": gain,
        "brightness": brightness,
        "contrast": contrast,
        "exposure": exposure,
        "auto_exposure": auto_exposure,
    }


def build_frame_enhancement_config(
    enabled=True,
    alpha=FRAME_ENHANCE_ALPHA,
    beta=FRAME_ENHANCE_BETA,
    gamma=FRAME_ENHANCE_GAMMA,
):
    return {
        "enabled": bool(enabled),
        "alpha": max(0.1, float(alpha)),
        "beta": float(beta),
        "gamma": max(0.05, float(gamma)),
    }


def enhance_frame_for_shadow(frame, frame_enhancement):
    if not frame_enhancement or not frame_enhancement["enabled"]:
        return frame

    alpha = frame_enhancement["alpha"]
    beta = frame_enhancement["beta"]
    gamma = frame_enhancement["gamma"]
    enhanced = frame

    if abs(alpha - 1.0) > 0.001 or abs(beta) > 0.001:
        enhanced = cv2.convertScaleAbs(enhanced, alpha=alpha, beta=beta)

    if abs(gamma - 1.0) > 0.001:
        table = ((np.arange(256, dtype=np.float32) / 255.0) ** gamma * 255.0)
        table = np.clip(table, 0, 255).astype(np.uint8)
        enhanced = cv2.LUT(enhanced, table)

    return enhanced


def noop_trackbar_callback(_value):
    return


def safe_trackbar_value(window_name, trackbar_name, default):
    try:
        return cv2.getTrackbarPos(trackbar_name, window_name)
    except cv2.error:
        return default


def create_shadow_control_window(camera_tuning, frame_enhancement):
    cv2.namedWindow(CONTROL_WINDOW_NAME, cv2.WINDOW_NORMAL)
    configure_preview_window(
        cv2,
        CONTROL_WINDOW_NAME,
        window_width=CONTROL_WINDOW_WIDTH,
        window_height=CONTROL_WINDOW_HEIGHT,
        offset_x=CONTROL_WINDOW_OFFSET_X,
        offset_y=CONTROL_WINDOW_OFFSET_Y,
    )

    cv2.createTrackbar(
        "Camera Tune",
        CONTROL_WINDOW_NAME,
        1 if camera_tuning and camera_tuning["enabled"] else 0,
        1,
        noop_trackbar_callback,
    )
    cv2.createTrackbar(
        "Gain",
        CONTROL_WINDOW_NAME,
        int(round(float(camera_tuning["gain"]))) if camera_tuning else 0,
        255,
        noop_trackbar_callback,
    )
    cv2.createTrackbar(
        "Brightness",
        CONTROL_WINDOW_NAME,
        int(round(float(camera_tuning["brightness"]))) if camera_tuning else 0,
        255,
        noop_trackbar_callback,
    )
    cv2.createTrackbar(
        "Contrast",
        CONTROL_WINDOW_NAME,
        int(round(float(camera_tuning["contrast"]))) if camera_tuning else 0,
        100,
        noop_trackbar_callback,
    )
    cv2.createTrackbar(
        "Frame Enhance",
        CONTROL_WINDOW_NAME,
        1 if frame_enhancement and frame_enhancement["enabled"] else 0,
        1,
        noop_trackbar_callback,
    )
    cv2.createTrackbar(
        "Alpha x100",
        CONTROL_WINDOW_NAME,
        int(round(float(frame_enhancement["alpha"]) * 100.0)) if frame_enhancement else 100,
        300,
        noop_trackbar_callback,
    )
    cv2.createTrackbar(
        "Beta +100",
        CONTROL_WINDOW_NAME,
        int(round(float(frame_enhancement["beta"]) + 100.0)) if frame_enhancement else 100,
        200,
        noop_trackbar_callback,
    )
    cv2.createTrackbar(
        "Gamma x100",
        CONTROL_WINDOW_NAME,
        int(round(float(frame_enhancement["gamma"]) * 100.0)) if frame_enhancement else 100,
        300,
        noop_trackbar_callback,
    )


def camera_tuning_signature(camera_tuning):
    if not camera_tuning:
        return None

    return (
        bool(camera_tuning["enabled"]),
        float(camera_tuning["gain"]),
        float(camera_tuning["brightness"]),
        float(camera_tuning["contrast"]),
        camera_tuning["exposure"],
        camera_tuning["auto_exposure"],
    )


def update_shadow_control_values(camera_tuning, frame_enhancement):
    if camera_tuning is not None:
        camera_tuning["enabled"] = safe_trackbar_value(CONTROL_WINDOW_NAME, "Camera Tune", 1) > 0
        camera_tuning["gain"] = float(safe_trackbar_value(CONTROL_WINDOW_NAME, "Gain", int(CAMERA_GAIN)))
        camera_tuning["brightness"] = float(
            safe_trackbar_value(CONTROL_WINDOW_NAME, "Brightness", int(CAMERA_BRIGHTNESS))
        )
        camera_tuning["contrast"] = float(
            safe_trackbar_value(CONTROL_WINDOW_NAME, "Contrast", int(CAMERA_CONTRAST))
        )

    if frame_enhancement is not None:
        frame_enhancement["enabled"] = safe_trackbar_value(CONTROL_WINDOW_NAME, "Frame Enhance", 1) > 0
        frame_enhancement["alpha"] = max(
            0.1,
            safe_trackbar_value(CONTROL_WINDOW_NAME, "Alpha x100", int(FRAME_ENHANCE_ALPHA * 100.0)) / 100.0,
        )
        frame_enhancement["beta"] = float(
            safe_trackbar_value(CONTROL_WINDOW_NAME, "Beta +100", int(FRAME_ENHANCE_BETA + 100.0)) - 100
        )
        frame_enhancement["gamma"] = max(
            0.05,
            safe_trackbar_value(CONTROL_WINDOW_NAME, "Gamma x100", int(FRAME_ENHANCE_GAMMA * 100.0)) / 100.0,
        )


def draw_sensitivity_overlay(display, camera_tuning, frame_enhancement):
    if camera_tuning is None and frame_enhancement is None:
        return

    height = display.shape[0]
    lines = []
    if camera_tuning is not None:
        lines.append(
            "cam "
            f"{'on' if camera_tuning['enabled'] else 'off'} | "
            f"gain {camera_tuning['gain']:.0f} | "
            f"bright {camera_tuning['brightness']:.0f} | "
            f"contrast {camera_tuning['contrast']:.0f}"
        )

    if frame_enhancement is not None:
        lines.append(
            "frame "
            f"{'on' if frame_enhancement['enabled'] else 'off'} | "
            f"alpha {frame_enhancement['alpha']:.2f} | "
            f"beta {frame_enhancement['beta']:.0f} | "
            f"gamma {frame_enhancement['gamma']:.2f}"
        )

    for index, line in enumerate(lines):
        cv2.putText(
            display,
            line,
            (10, height - 48 + (index * 24)),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            (255, 255, 255),
            2,
        )


def open_camera(
    camera_id=0,
    camera_tuning=None,
    width=CAMERA_WIDTH,
    height=CAMERA_HEIGHT,
    fps=CAMERA_FPS,
    buffer_size=CAMERA_BUFFER_SIZE,
):
    load_mesh_generation_dependencies()

    cap = create_capture(camera_id)

    if cap is None or not cap.isOpened():
        print(f"[ERROR] Camera {camera_id} could not be opened.")
        return None

    if hasattr(cv2, "VideoWriter_fourcc"):
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))

    set_capture_property(cap, cv2.CAP_PROP_BUFFERSIZE, buffer_size, "buffer size")
    set_capture_property(cap, cv2.CAP_PROP_FRAME_WIDTH, width, "width")
    set_capture_property(cap, cv2.CAP_PROP_FRAME_HEIGHT, height, "height")
    set_capture_property(cap, cv2.CAP_PROP_FPS, fps, "fps")
    apply_camera_sensitivity(cap, camera_tuning)

    ok, frame = read_valid_frame(cap)
    if not ok or frame is None or frame.size == 0:
        cap.release()
        print(f"[ERROR] Camera {camera_id} did not return a valid frame.")
        return None

    if is_nearly_black_frame(frame):
        print(
            f"[WARN] Camera {camera_id} returned a nearly black startup frame. "
            "The stream will auto-reopen if it stays black."
        )

    actual_width = int(round(cap.get(cv2.CAP_PROP_FRAME_WIDTH)))
    actual_height = int(round(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)))
    actual_fps = cap.get(cv2.CAP_PROP_FPS)
    print(f"[OK] Camera {camera_id} ready at {actual_width}x{actual_height} @ {actual_fps:.1f} fps.")
    return cap


def get_background_path():
    return os.path.join(OUTPUT_DIR, BACKGROUND_FILE_NAME)


def save_background_frame(frame):
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    background_path = get_background_path()
    if not cv2.imwrite(background_path, frame):
        print(f"[ERROR] Background image could not be saved: {background_path}")
        return

    print(f"[OK] Saved {background_path}")


def clamp_ratio(value, default, min_value=0.01, max_value=1.0):
    if value is None:
        return default

    return max(min_value, min(max_value, value))


def build_forearm_trim_config(
    enabled=FOREARM_TRIM_ENABLED,
    center_x_ratio=FOREARM_TRIM_CENTER_X_RATIO,
    center_y_ratio=FOREARM_TRIM_CENTER_Y_RATIO,
    radius_ratio=FOREARM_TRIM_RADIUS_RATIO,
):
    return {
        "enabled": bool(enabled),
        "center_x_ratio": clamp_ratio(center_x_ratio, FOREARM_TRIM_CENTER_X_RATIO),
        "center_y_ratio": clamp_ratio(center_y_ratio, FOREARM_TRIM_CENTER_Y_RATIO),
        "radius_ratio": clamp_ratio(radius_ratio, FOREARM_TRIM_RADIUS_RATIO),
    }


def get_forearm_trim_circle(frame_shape, trim_config):
    height, width = frame_shape[:2]
    center = (
        int(round(width * trim_config["center_x_ratio"])),
        int(round(height * trim_config["center_y_ratio"])),
    )
    radius = max(1, int(round(min(width, height) * trim_config["radius_ratio"])))
    return center, radius


def draw_forearm_trim_overlay(display, trim_config):
    if trim_config is None or not trim_config["enabled"]:
        return

    center, radius = get_forearm_trim_circle(display.shape, trim_config)
    cv2.circle(display, center, radius, (0, 220, 255), 2)
    cv2.putText(display, "keep hand inside yellow circle", (10, 60),
                cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 220, 255), 2)


def build_forearm_keep_mask(mask_shape, trim_config):
    keep_mask = np.zeros(mask_shape[:2], dtype=np.uint8)
    center, radius = get_forearm_trim_circle(mask_shape, trim_config)
    cv2.circle(keep_mask, center, radius, 255, -1)
    return keep_mask


def get_stillness_sample_size(frame_shape):
    height, width = frame_shape[:2]
    if width <= SHADOW_STILLNESS_SAMPLE_WIDTH:
        return width, height

    sample_height = max(1, int(round(height * SHADOW_STILLNESS_SAMPLE_WIDTH / width)))
    return SHADOW_STILLNESS_SAMPLE_WIDTH, sample_height


def build_stillness_roi_mask(frame_shape, trim_config):
    if trim_config is None or not trim_config["enabled"]:
        return None

    roi_mask = build_forearm_keep_mask(frame_shape, trim_config)
    sample_size = get_stillness_sample_size(frame_shape)
    if roi_mask.shape[1] != sample_size[0] or roi_mask.shape[0] != sample_size[1]:
        roi_mask = cv2.resize(roi_mask, sample_size, interpolation=cv2.INTER_NEAREST)

    return roi_mask


def trim_forearm_tail(mask, trim_config):
    if trim_config is None or not trim_config["enabled"]:
        return mask

    foreground_pixels = cv2.countNonZero(mask)
    if foreground_pixels <= 0:
        return mask

    keep_mask = build_forearm_keep_mask(mask.shape, trim_config)
    trimmed = cv2.bitwise_and(mask, keep_mask)

    kernel_size = max(1, FOREARM_TRIM_CLOSE_KERNEL)
    if kernel_size % 2 == 0:
        kernel_size += 1

    kernel = cv2.getStructuringElement(
        cv2.MORPH_ELLIPSE,
        (kernel_size, kernel_size)
    )
    trimmed = cv2.morphologyEx(trimmed, cv2.MORPH_CLOSE, kernel)

    remaining_pixels = cv2.countNonZero(trimmed)
    remaining_ratio = remaining_pixels / foreground_pixels
    if remaining_ratio < FOREARM_TRIM_MIN_REMAINING_RATIO:
        print("[WARN] Forearm trim removed too much of the shadow; using the original mask.")
        return mask

    removed_pixels = foreground_pixels - remaining_pixels
    print(
        "[INFO] Forearm trim kept "
        f"{remaining_pixels}/{foreground_pixels} shadow pixels "
        f"({remaining_ratio:.1%}); removed {removed_pixels}."
    )
    return trimmed


def build_shadow_auto_capture_config(
    enabled=SHADOW_AUTO_CAPTURE_ENABLED,
    hold_seconds=SHADOW_AUTO_CAPTURE_SECONDS,
    diff_threshold=SHADOW_STILLNESS_DIFF_THRESHOLD,
    motion_ratio=SHADOW_STILLNESS_MOTION_RATIO,
    presence_diff_threshold=SHADOW_PRESENCE_DIFF_THRESHOLD,
    presence_ratio=SHADOW_PRESENCE_MIN_RATIO,
):
    hold_seconds = SHADOW_AUTO_CAPTURE_SECONDS if hold_seconds is None else hold_seconds
    diff_threshold = SHADOW_STILLNESS_DIFF_THRESHOLD if diff_threshold is None else diff_threshold
    motion_ratio = SHADOW_STILLNESS_MOTION_RATIO if motion_ratio is None else motion_ratio
    presence_diff_threshold = (
        SHADOW_PRESENCE_DIFF_THRESHOLD
        if presence_diff_threshold is None
        else presence_diff_threshold
    )
    presence_ratio = SHADOW_PRESENCE_MIN_RATIO if presence_ratio is None else presence_ratio

    return {
        "enabled": bool(enabled),
        "hold_seconds": max(0.1, float(hold_seconds)),
        "diff_threshold": max(1, int(diff_threshold)),
        "motion_ratio": clamp_ratio(float(motion_ratio), SHADOW_STILLNESS_MOTION_RATIO, 0.0, 1.0),
        "presence_diff_threshold": max(1, int(presence_diff_threshold)),
        "presence_ratio": clamp_ratio(float(presence_ratio), SHADOW_PRESENCE_MIN_RATIO, 0.0, 1.0),
    }


def build_background_auto_capture_config(enabled=False, hold_seconds=BACKGROUND_AUTO_CAPTURE_SECONDS):
    hold_seconds = BACKGROUND_AUTO_CAPTURE_SECONDS if hold_seconds is None else hold_seconds
    return {
        "enabled": bool(enabled),
        "hold_seconds": max(0.1, float(hold_seconds)),
        "diff_threshold": SHADOW_STILLNESS_DIFF_THRESHOLD,
        "motion_ratio": SHADOW_STILLNESS_MOTION_RATIO,
    }


def prepare_stillness_frame(frame, roi_mask=None):
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    sample_size = get_stillness_sample_size(gray.shape)
    if gray.shape[1] != sample_size[0] or gray.shape[0] != sample_size[1]:
        gray = cv2.resize(gray, sample_size)

    if roi_mask is not None:
        if roi_mask.shape != gray.shape:
            roi_mask = cv2.resize(
                roi_mask,
                (gray.shape[1], gray.shape[0]),
                interpolation=cv2.INTER_NEAREST
            )
        gray = cv2.bitwise_and(gray, roi_mask)

    return cv2.GaussianBlur(gray, (5, 5), 0)


def calculate_binary_ratio(binary_mask, roi_mask=None):
    if roi_mask is None:
        return cv2.countNonZero(binary_mask) / binary_mask.size

    if roi_mask.shape != binary_mask.shape:
        roi_mask = cv2.resize(
            roi_mask,
            (binary_mask.shape[1], binary_mask.shape[0]),
            interpolation=cv2.INTER_NEAREST
        )

    roi_pixels = cv2.countNonZero(roi_mask)
    if roi_pixels <= 0:
        return 0.0

    masked_binary = cv2.bitwise_and(binary_mask, roi_mask)
    return cv2.countNonZero(masked_binary) / roi_pixels


def calculate_motion_ratio(previous_frame, current_frame, diff_threshold, roi_mask=None):
    diff = cv2.absdiff(previous_frame, current_frame)
    _, moving = cv2.threshold(diff, diff_threshold, 255, cv2.THRESH_BINARY)
    return calculate_binary_ratio(moving, roi_mask)


def calculate_shadow_presence_ratio(background_frame, current_frame, diff_threshold, roi_mask=None):
    diff = cv2.absdiff(background_frame, current_frame)
    _, shadow = cv2.threshold(diff, diff_threshold, 255, cv2.THRESH_BINARY)
    return calculate_binary_ratio(shadow, roi_mask)


def update_shadow_stillness(
    frame,
    background_frame,
    previous_frame,
    stable_started_at,
    auto_capture_config,
    roi_mask=None,
):
    current_frame = prepare_stillness_frame(frame, roi_mask)
    now = time.monotonic()
    shadow_ratio = 1.0
    shadow_present = True

    if background_frame is not None:
        shadow_ratio = calculate_shadow_presence_ratio(
            background_frame,
            current_frame,
            auto_capture_config["presence_diff_threshold"],
            roi_mask
        )
        shadow_present = shadow_ratio >= auto_capture_config["presence_ratio"]

    if not shadow_present:
        return None, None, 0.0, 1.0, shadow_ratio, False

    if previous_frame is None:
        return current_frame, now, 0.0, 0.0, shadow_ratio, True

    motion_ratio = calculate_motion_ratio(
        previous_frame,
        current_frame,
        auto_capture_config["diff_threshold"],
        roi_mask
    )

    if motion_ratio <= auto_capture_config["motion_ratio"]:
        if stable_started_at is None:
            stable_started_at = now
        stable_seconds = now - stable_started_at
    else:
        stable_started_at = None
        stable_seconds = 0.0

    return current_frame, stable_started_at, stable_seconds, motion_ratio, shadow_ratio, True


def update_frame_stillness(frame, previous_frame, stable_started_at, stillness_config, roi_mask=None):
    current_frame = prepare_stillness_frame(frame, roi_mask)
    now = time.monotonic()

    if previous_frame is None:
        return current_frame, now, 0.0, 0.0

    motion_ratio = calculate_motion_ratio(
        previous_frame,
        current_frame,
        stillness_config["diff_threshold"],
        roi_mask
    )

    if motion_ratio <= stillness_config["motion_ratio"]:
        if stable_started_at is None:
            stable_started_at = now
        stable_seconds = now - stable_started_at
    else:
        stable_started_at = None
        stable_seconds = 0.0

    return current_frame, stable_started_at, stable_seconds, motion_ratio


def draw_shadow_auto_capture_overlay(
    display,
    auto_capture_config,
    stable_seconds,
    motion_ratio,
    shadow_ratio,
    shadow_present,
    shadow_arm_remaining=0.0,
    shadow_confirm_seconds=0.0,
    shadow_confirm_required_seconds=SHADOW_CONFIRM_SECONDS,
):
    if auto_capture_config is None or not auto_capture_config["enabled"]:
        return

    hold_seconds = auto_capture_config["hold_seconds"]
    progress = min(stable_seconds, hold_seconds)
    waiting_for_arm = shadow_arm_remaining > 0.0
    color = (0, 160, 255) if waiting_for_arm else ((0, 255, 0) if shadow_present and stable_seconds > 0.0 else (0, 160, 255))
    if waiting_for_arm:
        status = f"Place shadow now: starts in {shadow_arm_remaining:.1f}s"
    elif shadow_present and shadow_confirm_seconds < shadow_confirm_required_seconds:
        status = f"Confirming shadow: {shadow_confirm_seconds:.1f}/{shadow_confirm_required_seconds:.1f}s"
    else:
        status = "Hold still in circle" if shadow_present else "Waiting for strong shadow in circle"
    cv2.putText(
        display,
        f"{status}: {progress:.1f}/{hold_seconds:.1f}s",
        (10, 90),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.65,
        color,
        2
    )
    cv2.putText(
        display,
        f"shadow {shadow_ratio * 100.0:.2f}% | motion {motion_ratio * 100.0:.2f}% | ENTER manual",
        (10, 120),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.55,
        color,
        2
    )


def draw_background_auto_capture_overlay(display, background_auto_capture_config, stable_seconds, motion_ratio):
    if background_auto_capture_config is None or not background_auto_capture_config["enabled"]:
        return

    hold_seconds = background_auto_capture_config["hold_seconds"]
    progress = min(stable_seconds, hold_seconds)
    cv2.putText(
        display,
        f"Auto background: keep empty scene still {progress:.1f}/{hold_seconds:.1f}s",
        (10, 90),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.65,
        (0, 255, 0) if stable_seconds > 0.0 else (0, 160, 255),
        2
    )
    cv2.putText(
        display,
        f"motion {motion_ratio * 100.0:.2f}% | ENTER manual",
        (10, 120),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.55,
        (0, 255, 0),
        2
    )


def capture_live(camera_id=0, trim_config=None,
                 auto_capture_config=None, background_auto_capture_config=None,
                 shadow_arm_delay_seconds=SHADOW_ARM_DELAY_SECONDS,
                 shadow_confirm_seconds=SHADOW_CONFIRM_SECONDS,
                 camera_tuning=None,
                 frame_enhancement=None,
                 show_control_window=True,
                 camera_width=CAMERA_WIDTH,
                 camera_height=CAMERA_HEIGHT,
                 camera_fps=CAMERA_FPS,
                 camera_buffer_size=CAMERA_BUFFER_SIZE):
    cap = open_camera(
        camera_id,
        camera_tuning,
        width=camera_width,
        height=camera_height,
        fps=camera_fps,
        buffer_size=camera_buffer_size,
    )
    if not cap:
        return None, None

    def reopen_camera_stream(reason):
        print(f"[WARN] Camera stream stalled ({reason}); reopening camera.")
        try:
            cap.release()
        except cv2.error:
            pass

        reopened = open_camera(
            camera_id,
            camera_tuning,
            width=camera_width,
            height=camera_height,
            fps=camera_fps,
            buffer_size=camera_buffer_size,
        )
        if reopened is None:
            print("[WARN] Camera reopen failed; retrying.")
            time.sleep(CAMERA_REOPEN_BACKOFF_SECONDS)
            return None

        print("[OK] Camera stream reopened.")
        return reopened

    bg_frame = None
    shadow_frame = None

    print("=" * 60)
    print("  Shadow Mesh Capture")
    print("=" * 60)
    if background_auto_capture_config is not None and background_auto_capture_config["enabled"]:
        print(
            "  Background: keep the empty scene still for "
            f"{background_auto_capture_config['hold_seconds']:.1f}s to auto-capture"
        )
    else:
        print("  ENTER: capture background")
    if auto_capture_config is not None and auto_capture_config["enabled"]:
        print(
            "  Shadow: show a shadow inside the yellow circle, then hold still for "
            f"{auto_capture_config['hold_seconds']:.1f}s to auto-capture"
        )
    else:
        print("  Shadow: ENTER to capture manually")
    print("  q: cancel")
    if camera_tuning is not None and camera_tuning["enabled"]:
        print(
            "  Camera tuning: "
            f"gain={camera_tuning['gain']}, brightness={camera_tuning['brightness']}, "
            f"contrast={camera_tuning['contrast']}"
        )
    if frame_enhancement is not None and frame_enhancement["enabled"]:
        print(
            "  Frame enhance: "
            f"alpha={frame_enhancement['alpha']}, beta={frame_enhancement['beta']}, "
            f"gamma={frame_enhancement['gamma']}"
        )
    print("=" * 60)

    step = 1
    stillness_roi_mask = build_stillness_roi_mask(bg_frame.shape, trim_config) if bg_frame is not None else None
    background_stillness_frame = (
        prepare_stillness_frame(bg_frame, stillness_roi_mask)
        if bg_frame is not None
        else None
    )
    previous_stillness_frame = None
    stable_started_at = None
    stable_seconds = 0.0
    motion_ratio = 1.0
    shadow_ratio = 0.0
    shadow_present = False
    previous_background_stillness_frame = None
    background_stable_started_at = None
    background_stable_seconds = 0.0
    background_motion_ratio = 1.0
    shadow_auto_capture_enabled_at = None
    shadow_presence_started_at = None
    shadow_presence_seconds = 0.0

    cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
    configure_preview_window(cv2, PREVIEW_WINDOW_NAME)
    use_control_window = bool(show_control_window and (camera_tuning is not None or frame_enhancement is not None))
    last_camera_signature = camera_tuning_signature(camera_tuning)
    if use_control_window:
        create_shadow_control_window(camera_tuning, frame_enhancement)

    last_valid_frame_at = time.perf_counter()
    black_frame_started_at = None
    last_reopen_attempt_at = 0.0

    while True:
        if use_control_window:
            update_shadow_control_values(camera_tuning, frame_enhancement)
            current_camera_signature = camera_tuning_signature(camera_tuning)
            if current_camera_signature != last_camera_signature:
                if camera_tuning is not None and camera_tuning["enabled"]:
                    apply_camera_sensitivity(cap, camera_tuning)

                last_camera_signature = current_camera_signature

        ret, frame = cap.read()
        now = time.perf_counter()
        if not ret or frame is None or frame.size == 0:
            if (
                now - last_valid_frame_at >= CAMERA_NO_FRAME_REOPEN_SECONDS and
                now - last_reopen_attempt_at >= CAMERA_REOPEN_BACKOFF_SECONDS
            ):
                last_reopen_attempt_at = now
                reopened = reopen_camera_stream("no valid frames")
                if reopened is not None:
                    cap = reopened
                    last_valid_frame_at = time.perf_counter()
                    black_frame_started_at = None
                continue

            time.sleep(0.01)
            continue

        last_valid_frame_at = now
        if is_nearly_black_frame(frame):
            if black_frame_started_at is None:
                black_frame_started_at = now
            elif (
                now - black_frame_started_at >= CAMERA_BLACK_FRAME_REOPEN_SECONDS and
                now - last_reopen_attempt_at >= CAMERA_REOPEN_BACKOFF_SECONDS
            ):
                last_reopen_attempt_at = now
                reopened = reopen_camera_stream("nearly black frames")
                if reopened is not None:
                    cap = reopened
                    last_valid_frame_at = time.perf_counter()
                    black_frame_started_at = None
                continue
        else:
            black_frame_started_at = None

        frame = enhance_frame_for_shadow(frame, frame_enhancement)

        if (
            step == 1 and
            background_auto_capture_config is not None and
            background_auto_capture_config["enabled"]
        ):
            (
                previous_background_stillness_frame,
                background_stable_started_at,
                background_stable_seconds,
                background_motion_ratio,
            ) = update_frame_stillness(
                frame,
                previous_background_stillness_frame,
                background_stable_started_at,
                background_auto_capture_config,
                None
            )
            if background_stable_seconds >= background_auto_capture_config["hold_seconds"]:
                bg_frame = frame.copy()
                print(
                    "[OK] Background auto-captured after "
                    f"{background_auto_capture_config['hold_seconds']:.1f}s of stillness."
                )
                save_background_frame(bg_frame)
                step = 2
                stillness_roi_mask = build_stillness_roi_mask(bg_frame.shape, trim_config)
                background_stillness_frame = prepare_stillness_frame(bg_frame, stillness_roi_mask)
                previous_stillness_frame = None
                stable_started_at = None
                stable_seconds = 0.0
                motion_ratio = 1.0
                shadow_ratio = 0.0
                shadow_present = False
                shadow_auto_capture_enabled_at = time.monotonic() + max(0.0, shadow_arm_delay_seconds)
                shadow_presence_started_at = None
                shadow_presence_seconds = 0.0

        if step == 2 and auto_capture_config is not None and auto_capture_config["enabled"]:
            now = time.monotonic()
            shadow_auto_capture_armed = (
                shadow_auto_capture_enabled_at is None or
                now >= shadow_auto_capture_enabled_at
            )

            if shadow_auto_capture_armed:
                (
                    previous_stillness_frame,
                    stable_started_at,
                    stable_seconds,
                    motion_ratio,
                    shadow_ratio,
                    shadow_present,
                ) = update_shadow_stillness(
                    frame,
                    background_stillness_frame,
                    previous_stillness_frame,
                    stable_started_at,
                    auto_capture_config,
                    stillness_roi_mask
                )
                if shadow_present:
                    if shadow_presence_started_at is None:
                        shadow_presence_started_at = now
                    shadow_presence_seconds = now - shadow_presence_started_at
                else:
                    shadow_presence_started_at = None
                    shadow_presence_seconds = 0.0

                shadow_confirmed = shadow_presence_seconds >= max(0.0, shadow_confirm_seconds)
                if not shadow_confirmed:
                    stable_started_at = None
                    stable_seconds = 0.0

                if shadow_confirmed and stable_seconds >= auto_capture_config["hold_seconds"]:
                    shadow_frame = frame.copy()
                    print(
                        "[OK] Shadow auto-captured after "
                        f"{auto_capture_config['hold_seconds']:.1f}s of stillness."
                    )
                    break
            else:
                previous_stillness_frame = None
                stable_started_at = None
                stable_seconds = 0.0
                motion_ratio = 1.0
                shadow_ratio = 0.0
                shadow_present = False
                shadow_presence_started_at = None
                shadow_presence_seconds = 0.0

        display = frame.copy()

        if step == 1:
            text = (
                "Step 1: Auto-capturing BACKGROUND (empty scene)"
                if background_auto_capture_config is not None and background_auto_capture_config["enabled"]
                else "Step 1: Press ENTER to capture BACKGROUND (no object)"
            )
        elif step == 2:
            text = "Step 2: Place shadow, hold still for auto capture"
        else:
            text = "Done! Processing..."

        cv2.putText(display, text, (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)

        if bg_frame is not None and step == 2:
            bg_small = cv2.resize(bg_frame, (160, 120))
            display[10:130, display.shape[1]-170:display.shape[1]-10] = bg_small
            cv2.putText(display, "BG", (display.shape[1]-160, 25),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)

        draw_forearm_trim_overlay(display, trim_config)
        if step == 1:
            draw_background_auto_capture_overlay(
                display,
                background_auto_capture_config,
                background_stable_seconds,
                background_motion_ratio
            )
        if step == 2:
            shadow_arm_remaining = 0.0
            if shadow_auto_capture_enabled_at is not None:
                shadow_arm_remaining = max(0.0, shadow_auto_capture_enabled_at - time.monotonic())
            draw_shadow_auto_capture_overlay(
                display,
                auto_capture_config,
                stable_seconds,
                motion_ratio,
                shadow_ratio,
                shadow_present,
                shadow_arm_remaining,
                shadow_presence_seconds,
                shadow_confirm_seconds
            )

        draw_sensitivity_overlay(display, camera_tuning, frame_enhancement)
        cv2.imshow(PREVIEW_WINDOW_NAME, display)
        key = cv2.waitKey(1) & 0xFF

        if key == QUIT_KEY:
            print("[INFO] Capture canceled.")
            cap.release()
            cv2.destroyAllWindows()
            return None, None

        if key in ENTER_KEYS:
            if step == 1:
                bg_frame = frame.copy()
                print("[OK] Background captured.")
                save_background_frame(bg_frame)
                step = 2
                stillness_roi_mask = build_stillness_roi_mask(bg_frame.shape, trim_config)
                background_stillness_frame = prepare_stillness_frame(bg_frame, stillness_roi_mask)
                previous_stillness_frame = None
                stable_started_at = None
                stable_seconds = 0.0
                motion_ratio = 1.0
                shadow_ratio = 0.0
                shadow_present = False
                previous_background_stillness_frame = None
                background_stable_started_at = None
                background_stable_seconds = 0.0
                background_motion_ratio = 1.0
                shadow_auto_capture_enabled_at = time.monotonic() + max(0.0, shadow_arm_delay_seconds)
                shadow_presence_started_at = None
                shadow_presence_seconds = 0.0
            elif step == 2:
                shadow_frame = frame.copy()
                print("[OK] Shadow captured.")
                break

    cap.release()
    cv2.destroyAllWindows()
    return bg_frame, shadow_frame


def extract_shadow_mask(bg_frame, shadow_frame):
    bg_gray = cv2.cvtColor(bg_frame, cv2.COLOR_BGR2GRAY)
    sh_gray = cv2.cvtColor(shadow_frame, cv2.COLOR_BGR2GRAY)

    diff = cv2.absdiff(bg_gray, sh_gray)
    blurred = cv2.GaussianBlur(diff, (7, 7), 0)
    _, mask = cv2.threshold(blurred, 0, 255,
                            cv2.THRESH_BINARY + cv2.THRESH_OTSU)

    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)

    return mask


def extract_contour(mask, epsilon_ratio=0.005):
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL,
                                   cv2.CHAIN_APPROX_SIMPLE)

    if len(contours) == 0:
        print("[ERROR] Shadow contour was not found.")
        return None

    main_contour = max(contours, key=cv2.contourArea)

    perimeter = cv2.arcLength(main_contour, True)
    epsilon = epsilon_ratio * perimeter
    simplified = cv2.approxPolyDP(main_contour, epsilon, True)

    contour = simplified.reshape(-1, 2)

    return contour


def generate_mesh(contour, interior_spacing=7):
    boundary = contour.astype(np.float64)
    n_boundary = len(boundary)

    x_min, y_min = boundary.min(axis=0)
    x_max, y_max = boundary.max(axis=0)

    xs = np.arange(x_min + interior_spacing/2, x_max, interior_spacing)
    ys = np.arange(y_min + interior_spacing/2, y_max, interior_spacing)
    grid_x, grid_y = np.meshgrid(xs, ys)
    grid_points = np.column_stack([grid_x.ravel(), grid_y.ravel()])

    contour_cv = boundary.reshape(-1, 1, 2).astype(np.float32)
    interior = []
    for pt in grid_points:
        dist = cv2.pointPolygonTest(contour_cv, (float(pt[0]), float(pt[1])), False)
        if dist > 0:
            interior.append(pt)

    interior = np.array(interior) if len(interior) > 0 else np.empty((0, 2))

    all_points = np.vstack([boundary, interior]) if len(interior) > 0 else boundary

    segments = np.array([(i, (i+1) % n_boundary) for i in range(n_boundary)])

    tri_input = {
        'vertices': all_points,
        'segments': segments
    }

    tri_result = tr.triangulate(tri_input, 'p')

    vertices_2d = tri_result['vertices']
    faces = tri_result['triangles']

    valid_faces = []
    for face in faces:
        centroid = np.mean(vertices_2d[face], axis=0)
        dist = cv2.pointPolygonTest(contour_cv,
                                     (float(centroid[0]), float(centroid[1])),
                                     False)
        if dist >= 0:
            valid_faces.append(face)

    valid_faces = np.array(valid_faces)

    center = np.mean(vertices_2d, axis=0)
    vertices_normalized = vertices_2d - center
    scale = np.max(vertices_normalized.max(axis=0) - vertices_normalized.min(axis=0))
    if scale > 0:
        vertices_normalized /= scale

    vertices_normalized[:, 1] *= -1.0
    if len(valid_faces) > 0:
        valid_faces = valid_faces[:, [0, 2, 1]]

    vertices_3d = np.column_stack([
        vertices_normalized,
        np.zeros(len(vertices_normalized))
    ])

    return vertices_3d, valid_faces, n_boundary, center, scale


def save_contour_image(filepath, mask, contour):
    preview = np.zeros((mask.shape[0], mask.shape[1], 3), dtype=np.uint8)
    cv2.drawContours(preview, [contour.astype(np.int32)], -1, (255, 255, 255), -1)
    cv2.imwrite(filepath, preview)


def save_obj(filepath, vertices, faces):
    temp_filepath = f"{filepath}.tmp"
    try:
        with open(temp_filepath, 'w', encoding='utf-8', newline='\n') as f:
            for v in vertices:
                f.write(f"v {v[0]:.6f} {v[1]:.6f} {v[2]:.6f}\n")

            f.write("\n")

            for face in faces:
                f.write(f"f {face[0]+1} {face[1]+1} {face[2]+1}\n")

        os.replace(temp_filepath, filepath)
    finally:
        if os.path.exists(temp_filepath):
            os.remove(temp_filepath)


def save_metadata(filepath, n_vertices, n_faces, n_boundary, center, scale,
                  frame_width, frame_height, epsilon_ratio, interior_spacing,
                  trim_config):
    metadata = {
        "n_vertices": n_vertices,
        "n_triangles": n_faces,
        "n_boundary": n_boundary,
        "boundary_indices": list(range(n_boundary)),
        "center_offset": center.tolist(),
        "scale_factor": float(scale),
        "frame_width": int(frame_width),
        "frame_height": int(frame_height),
        "epsilon_ratio": epsilon_ratio,
        "interior_spacing": interior_spacing,
        "forearm_trim_enabled": bool(trim_config and trim_config["enabled"]),
        "forearm_trim_center": [
            float(trim_config["center_x_ratio"]) if trim_config else FOREARM_TRIM_CENTER_X_RATIO,
            float(trim_config["center_y_ratio"]) if trim_config else FOREARM_TRIM_CENTER_Y_RATIO,
        ],
        "forearm_trim_radius_ratio": (
            float(trim_config["radius_ratio"]) if trim_config else FOREARM_TRIM_RADIUS_RATIO
        ),
        "forearm_trim_radius": [
            float(trim_config["radius_ratio"]) if trim_config else FOREARM_TRIM_RADIUS_RATIO,
            float(trim_config["radius_ratio"]) if trim_config else FOREARM_TRIM_RADIUS_RATIO,
        ],
    }

    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(metadata, f, indent=2, ensure_ascii=False)


def process_shadow(bg_frame, shadow_frame, trim_config=None):
    load_mesh_generation_dependencies()
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    print("[INFO] Generating shadow mesh...")
    mask = extract_shadow_mask(bg_frame, shadow_frame)
    mask = trim_forearm_tail(mask, trim_config)

    contour = extract_contour(mask, EPSILON_RATIO)
    if contour is None:
        return False

    vertices_3d, faces, n_boundary, center, scale = generate_mesh(
        contour, INTERIOR_SPACING
    )

    contour_path = os.path.join(OUTPUT_DIR, CONTOUR_FILE_NAME)
    save_contour_image(contour_path, mask, contour)

    obj_path = os.path.join(OUTPUT_DIR, MESH_FILE_NAME)
    save_obj(obj_path, vertices_3d, faces)

    meta_path = os.path.join(OUTPUT_DIR, METADATA_FILE_NAME)
    frame_height, frame_width = mask.shape[:2]
    save_metadata(meta_path, len(vertices_3d), len(faces), n_boundary,
                  center, scale, frame_width, frame_height,
                  EPSILON_RATIO, INTERIOR_SPACING, trim_config)

    print(f"[OK] Saved {contour_path}")
    print(f"[OK] Saved {obj_path}")
    print(f"[OK] Saved {meta_path}")

    return True


def validate_existing_mesh():
    obj_path = os.path.join(OUTPUT_DIR, MESH_FILE_NAME)
    metadata_path = os.path.join(OUTPUT_DIR, METADATA_FILE_NAME)

    if not os.path.isfile(obj_path):
        print(f"[ERROR] Existing mesh was not found: {obj_path}")
        return False

    if not os.path.isfile(metadata_path):
        print(f"[ERROR] Existing metadata was not found: {metadata_path}")
        return False

    if os.path.getsize(obj_path) <= 0 or os.path.getsize(metadata_path) <= 0:
        print("[ERROR] Existing shadow mesh files are empty.")
        return False

    try:
        with open(metadata_path, "r", encoding="utf-8") as f:
            json.load(f)
    except (OSError, json.JSONDecodeError):
        print(f"[ERROR] Existing metadata could not be read: {metadata_path}")
        return False

    print(f"[OK] Using existing {obj_path}")
    print(f"[OK] Using existing {metadata_path}")
    return True


def main():
    parser = argparse.ArgumentParser(description="Capture a shadow mesh for Unity.")
    parser.add_argument("--mode", choices=["live", "file"], default="live")
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--camera-width", type=int, default=CAMERA_WIDTH)
    parser.add_argument("--camera-height", type=int, default=CAMERA_HEIGHT)
    parser.add_argument("--camera-fps", type=int, default=CAMERA_FPS)
    parser.add_argument("--camera-buffer-size", type=int, default=CAMERA_BUFFER_SIZE)
    parser.add_argument("--bg", dest="background_auto_capture", action="store_true")
    parser.add_argument("--no-camera-tuning", dest="camera_tuning", action="store_false")
    parser.add_argument("--camera-gain", type=float, default=CAMERA_GAIN)
    parser.add_argument("--camera-brightness", type=float, default=CAMERA_BRIGHTNESS)
    parser.add_argument("--camera-contrast", type=float, default=CAMERA_CONTRAST)
    parser.add_argument("--camera-exposure", type=float, default=CAMERA_EXPOSURE)
    parser.add_argument("--camera-auto-exposure", type=float, default=CAMERA_AUTO_EXPOSURE)
    parser.add_argument("--no-frame-enhance", dest="frame_enhance", action="store_false")
    parser.add_argument("--no-control-window", dest="control_window", action="store_false")
    parser.add_argument("--frame-alpha", type=float, default=FRAME_ENHANCE_ALPHA)
    parser.add_argument("--frame-beta", type=float, default=FRAME_ENHANCE_BETA)
    parser.add_argument("--frame-gamma", type=float, default=FRAME_ENHANCE_GAMMA)
    parser.add_argument("--presence-threshold", type=int, default=SHADOW_PRESENCE_DIFF_THRESHOLD)
    parser.add_argument("--presence-ratio", type=float, default=SHADOW_PRESENCE_MIN_RATIO)
    parser.add_argument("--stillness-threshold", type=int, default=SHADOW_STILLNESS_DIFF_THRESHOLD)
    parser.add_argument("--motion-ratio", type=float, default=SHADOW_STILLNESS_MOTION_RATIO)
    parser.set_defaults(background_auto_capture=False)
    parser.set_defaults(camera_tuning=True)
    parser.set_defaults(frame_enhance=True)
    parser.set_defaults(control_window=True)

    args = parser.parse_args()
    trim_config = build_forearm_trim_config()
    camera_tuning = build_camera_tuning_config(
        enabled=args.camera_tuning,
        gain=args.camera_gain,
        brightness=args.camera_brightness,
        contrast=args.camera_contrast,
        exposure=args.camera_exposure,
        auto_exposure=args.camera_auto_exposure,
    )
    frame_enhancement = build_frame_enhancement_config(
        enabled=args.frame_enhance,
        alpha=args.frame_alpha,
        beta=args.frame_beta,
        gamma=args.frame_gamma,
    )
    auto_capture_config = build_shadow_auto_capture_config(
        diff_threshold=args.stillness_threshold,
        motion_ratio=args.motion_ratio,
        presence_diff_threshold=args.presence_threshold,
        presence_ratio=args.presence_ratio,
    )
    background_auto_capture_config = build_background_auto_capture_config(
        enabled=args.background_auto_capture,
    )

    if args.mode == "live":
        bg_frame, shadow_frame = capture_live(
            args.camera,
            trim_config,
            auto_capture_config,
            background_auto_capture_config,
            camera_tuning=camera_tuning,
            frame_enhancement=frame_enhancement,
            show_control_window=args.control_window,
            camera_width=args.camera_width,
            camera_height=args.camera_height,
            camera_fps=args.camera_fps,
            camera_buffer_size=args.camera_buffer_size,
        )
        if bg_frame is None or shadow_frame is None:
            sys.exit(1)
        success = process_shadow(bg_frame, shadow_frame, trim_config)

    elif args.mode == "file":
        success = validate_existing_mesh()

    if not success:
        sys.exit(1)


if __name__ == "__main__":
    main()
