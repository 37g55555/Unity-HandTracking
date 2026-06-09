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
CAMERA_WIDTH = 1920
CAMERA_HEIGHT = 1080
CAMERA_FPS = 15
EPSILON_RATIO = 0.0015
INTERIOR_SPACING = 7
FOREARM_TRIM_ENABLED = True
FOREARM_TRIM_CENTER_X_RATIO = 0.5
FOREARM_TRIM_CENTER_Y_RATIO = 0.5
FOREARM_TRIM_RADIUS_RATIO = 0.39
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


def get_camera_backend():
    if sys.platform.startswith("win") and hasattr(cv2, "CAP_DSHOW"):
        return cv2.CAP_DSHOW

    if sys.platform == "darwin" and hasattr(cv2, "CAP_AVFOUNDATION"):
        return cv2.CAP_AVFOUNDATION

    return None


def create_capture(camera_id):
    backend = get_camera_backend()
    return cv2.VideoCapture(camera_id, backend) if backend is not None else cv2.VideoCapture(camera_id)


def open_camera(camera_id=0):
    load_mesh_generation_dependencies()

    cap = create_capture(camera_id)

    if not cap.isOpened():
        print(f"[ERROR] Camera {camera_id} could not be opened.")
        return None

    cap.set(cv2.CAP_PROP_FRAME_WIDTH, CAMERA_WIDTH)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAMERA_HEIGHT)
    cap.set(cv2.CAP_PROP_FPS, CAMERA_FPS)

    ok, frame = cap.read()
    if not ok or frame is None or frame.size == 0:
        cap.release()
        print(f"[ERROR] Camera {camera_id} did not return a valid frame.")
        return None

    print(f"[OK] Camera {camera_id} ready.")
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
                 shadow_confirm_seconds=SHADOW_CONFIRM_SECONDS):
    cap = open_camera(camera_id)
    if not cap:
        return None, None

    bg_frame = None
    shadow_frame = None

    print("=" * 60)
    print("  Shadow Mesh Capture")
    print("=" * 60)
    print("  ENTER: capture background")
    if background_auto_capture_config is not None and background_auto_capture_config["enabled"]:
        print(
            "  Background: keep the empty scene still for "
            f"{background_auto_capture_config['hold_seconds']:.1f}s to auto-capture"
        )
    if auto_capture_config is not None and auto_capture_config["enabled"]:
        print(
            "  Shadow: show a shadow inside the yellow circle, then hold still for "
            f"{auto_capture_config['hold_seconds']:.1f}s to auto-capture"
        )
    else:
        print("  Shadow: ENTER to capture manually")
    print("  q: cancel")
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

    while True:
        ret, frame = cap.read()
        if not ret:
            break

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
            text = "Step 1: Press ENTER to capture BACKGROUND (no object)"
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
    parser.add_argument("--bg", dest="background_auto_capture", action="store_true")
    parser.set_defaults(background_auto_capture=False)

    args = parser.parse_args()
    trim_config = build_forearm_trim_config()
    auto_capture_config = build_shadow_auto_capture_config()
    background_auto_capture_config = build_background_auto_capture_config(
        enabled=args.background_auto_capture,
    )

    if args.mode == "live":
        bg_frame, shadow_frame = capture_live(
            args.camera,
            trim_config,
            auto_capture_config,
            background_auto_capture_config,
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
