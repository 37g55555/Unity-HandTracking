import argparse
import socket
import time

import cv2
import numpy as np

from camera_utils import open_latest_frame_camera
from preview_window_utils import (
    configure_preview_window,
    get_foreground_window,
    keep_preview_window_no_activate,
)

UDP_HOST = "127.0.0.1"
PREVIEW_WINDOW_NAME = "Shadow Area"
QUIT_KEY = ord("q")


def log(message):
    print(message, flush=True)


def build_roi_mask(frame_shape, roi, roi_circle, roi_ellipse):
    if roi_ellipse is not None:
        return build_ellipse_roi_mask(frame_shape, roi_ellipse)

    if roi_circle is not None:
        return build_circle_roi_mask(frame_shape, roi_circle)

    if roi is None:
        return None

    return build_rect_roi_mask(frame_shape, roi)


def build_rect_roi_mask(frame_shape, roi):
    height, width = frame_shape[:2]
    x0, y0, x1, y1 = roi
    x0 = int(np.clip(round(x0 * width), 0, width - 1))
    x1 = int(np.clip(round(x1 * width), x0 + 1, width))
    y0 = int(np.clip(round(y0 * height), 0, height - 1))
    y1 = int(np.clip(round(y1 * height), y0 + 1, height))

    mask = np.zeros((height, width), dtype=np.uint8)
    mask[y0:y1, x0:x1] = 255
    return mask


def build_circle_roi_mask(frame_shape, roi_circle):
    height, width = frame_shape[:2]
    center_x, center_y, radius = roi_circle
    center = (
        int(np.clip(round(center_x * width), 0, width - 1)),
        int(np.clip(round(center_y * height), 0, height - 1)),
    )
    pixel_radius = max(1, int(round(radius * min(width, height))))

    mask = np.zeros((height, width), dtype=np.uint8)
    cv2.circle(mask, center, pixel_radius, 255, -1)
    return mask


def build_ellipse_roi_mask(frame_shape, roi_ellipse):
    height, width = frame_shape[:2]
    center_x, center_y, radius_x, radius_y = roi_ellipse
    center = (
        int(np.clip(round(center_x * width), 0, width - 1)),
        int(np.clip(round(center_y * height), 0, height - 1)),
    )
    axes = (
        max(1, int(round(radius_x * width))),
        max(1, int(round(radius_y * height))),
    )

    mask = np.zeros((height, width), dtype=np.uint8)
    cv2.ellipse(mask, center, axes, 0, 0, 360, 255, -1)
    return mask


def parse_roi(value):
    if not value:
        return None

    parts = [float(part.strip()) for part in value.split(",")]
    if len(parts) != 4:
        raise argparse.ArgumentTypeError("ROI must be x0,y0,x1,y1.")

    x0, y0, x1, y1 = parts
    if x1 <= x0 or y1 <= y0:
        raise argparse.ArgumentTypeError("ROI max values must be greater than min values.")

    return (
        max(0.0, min(1.0, x0)),
        max(0.0, min(1.0, y0)),
        max(0.0, min(1.0, x1)),
        max(0.0, min(1.0, y1)),
    )


def parse_roi_circle(value):
    if not value:
        return None

    parts = [float(part.strip()) for part in value.split(",")]
    if len(parts) != 3:
        raise argparse.ArgumentTypeError("ROI circle must be cx,cy,r.")

    center_x, center_y, radius = parts
    if radius <= 0.0:
        raise argparse.ArgumentTypeError("ROI circle radius must be greater than 0.")

    return (
        max(0.0, min(1.0, center_x)),
        max(0.0, min(1.0, center_y)),
        max(0.001, min(1.0, radius)),
    )


def parse_roi_ellipse(value):
    if not value:
        return None

    parts = [float(part.strip()) for part in value.split(",")]
    if len(parts) != 4:
        raise argparse.ArgumentTypeError("ROI ellipse must be cx,cy,rx,ry.")

    center_x, center_y, radius_x, radius_y = parts
    if radius_x <= 0.0 or radius_y <= 0.0:
        raise argparse.ArgumentTypeError("ROI ellipse radii must be greater than 0.")

    return (
        max(0.0, min(1.0, center_x)),
        max(0.0, min(1.0, center_y)),
        max(0.001, min(1.0, radius_x)),
        max(0.001, min(1.0, radius_y)),
    )


def capture_background(cap, seconds, roi_mask, preview):
    started_at = time.monotonic()
    frames = []
    restore_focus_window = get_foreground_window() if preview else None
    preview_focus_restored = False

    log(f"[INFO] Capturing empty background for {seconds:.1f}s.")
    if preview:
        cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
        configure_preview_window(cv2, PREVIEW_WINDOW_NAME, restore_focus_window)

    while True:
        ok, frame = cap.read(copy_frame=preview)
        if not ok or frame is None:
            continue

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        frames.append(gray.astype(np.float32))

        if preview:
            display = dim_outside_roi(frame.copy(), roi_mask)
            remaining = max(0.0, seconds - (time.monotonic() - started_at))
            cv2.putText(
                display,
                f"Capturing background: {remaining:.1f}s",
                (24, 48),
                cv2.FONT_HERSHEY_SIMPLEX,
                1.0,
                (0, 255, 255),
                2,
            )

            if roi_mask is not None:
                overlay_roi(display, roi_mask, (0, 255, 255))

            cv2.imshow(PREVIEW_WINDOW_NAME, display)
            if not preview_focus_restored:
                keep_preview_window_no_activate(PREVIEW_WINDOW_NAME, restore_focus_window)
                preview_focus_restored = True

            if cv2.waitKey(1) & 0xFF == QUIT_KEY:
                raise KeyboardInterrupt

        if time.monotonic() - started_at >= seconds and frames:
            break

    background = np.mean(frames, axis=0).astype(np.float32)
    log(f"[OK] Background captured from {len(frames)} frame(s).")
    return background


def overlay_roi(display, roi_mask, color):
    contours, _ = cv2.findContours(roi_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    cv2.drawContours(display, contours, -1, color, 2)


def dim_outside_roi(display, roi_mask):
    if roi_mask is None:
        return display

    focused = display.copy()
    outside = roi_mask <= 0
    focused[outside] = (focused[outside].astype(np.float32) * 0.24).astype(np.uint8)
    return focused


def calculate_shadow_ratio(gray, background, roi_mask, darkening_threshold):
    shadow_mask = (background - gray.astype(np.float32)) >= darkening_threshold

    if roi_mask is not None:
        active = roi_mask > 0
        sample_count = int(np.count_nonzero(active))
        if sample_count <= 0:
            return 0.0, np.zeros_like(gray, dtype=np.uint8)

        ratio = float(np.count_nonzero(shadow_mask & active)) / sample_count
        mask = np.zeros_like(gray, dtype=np.uint8)
        mask[shadow_mask & active] = 255
        return ratio, mask

    ratio = float(np.count_nonzero(shadow_mask)) / shadow_mask.size
    mask = np.zeros_like(gray, dtype=np.uint8)
    mask[shadow_mask] = 255
    return ratio, mask


def draw_shadow_overlay(display, mask):
    if mask is None:
        return display

    overlay = display.copy()
    overlay[mask > 0] = (0, 0, 255)
    return cv2.addWeighted(overlay, 0.32, display, 0.68, 0)


def run_tracking(
    camera_id,
    udp_port,
    background_seconds,
    darkening_threshold,
    roi,
    roi_circle,
    roi_ellipse,
    width,
    height,
    fps,
    camera_buffer_size,
    camera_auto_exposure,
    camera_exposure,
    camera_autofocus,
    directshow_device,
    directshow_pixel_format,
    directshow_video_codec,
    allow_black_frames,
    preview,
):
    cap = open_latest_frame_camera(
        camera_id,
        width=width,
        height=height,
        fps=fps,
        buffer_size=camera_buffer_size,
        auto_exposure=camera_auto_exposure,
        exposure=camera_exposure,
        autofocus=camera_autofocus,
        directshow_device=directshow_device,
        directshow_pixel_format=directshow_pixel_format,
        directshow_video_codec=directshow_video_codec,
        allow_black_frames=allow_black_frames,
        log=log,
    )
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    udp_target = (UDP_HOST, udp_port)
    roi_mask = None

    try:
        ok, first_frame = cap.read()
        if ok and first_frame is not None:
            roi_mask = build_roi_mask(first_frame.shape, roi, roi_circle, roi_ellipse)

        background = capture_background(cap, background_seconds, roi_mask, preview)

        restore_focus_window = get_foreground_window() if preview else None
        preview_focus_restored = False
        if preview:
            cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
            configure_preview_window(cv2, PREVIEW_WINDOW_NAME, restore_focus_window)
        log(f"[OK] Sending shadow ratio to Unity UDP {UDP_HOST}:{udp_port}.")

        while True:
            ok, frame = cap.read(copy_frame=preview)
            if not ok or frame is None:
                continue

            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            ratio, mask = calculate_shadow_ratio(
                gray,
                background,
                roi_mask,
                darkening_threshold,
            )
            payload = f"ratio={ratio:.6f}"
            sock.sendto(payload.encode("utf-8"), udp_target)

            if preview:
                display = dim_outside_roi(frame.copy(), roi_mask)
                display = draw_shadow_overlay(display, mask)
                if roi_mask is not None:
                    overlay_roi(display, roi_mask, (0, 255, 255))

                cv2.putText(
                    display,
                    f"shadow area {ratio * 100.0:.2f}%",
                    (24, 48),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    1.0,
                    (0, 255, 0),
                    2,
                )

                cv2.imshow(PREVIEW_WINDOW_NAME, display)
                if not preview_focus_restored:
                    keep_preview_window_no_activate(PREVIEW_WINDOW_NAME, restore_focus_window)
                    preview_focus_restored = True

                if cv2.waitKey(1) & 0xFF == QUIT_KEY:
                    break
    finally:
        sock.close()
        cap.release()
        try:
            cv2.destroyWindow(PREVIEW_WINDOW_NAME)
        except cv2.error:
            pass


def main():
    parser = argparse.ArgumentParser(description="Measure live shadow area and send the ratio to Unity.")
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=360)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--camera-buffer-size", type=int, default=1)
    parser.add_argument("--camera-auto-exposure", type=float, default=0.75)
    parser.add_argument("--camera-exposure", type=float, default=None)
    parser.add_argument("--camera-autofocus", type=float, default=0.0)
    parser.add_argument("--directshow-device", default="")
    parser.add_argument("--directshow-pixel-format", default="")
    parser.add_argument("--directshow-video-codec", default="")
    parser.add_argument("--allow-black-frames", action="store_true")
    preview_group = parser.add_mutually_exclusive_group()
    preview_group.add_argument("--preview", dest="preview", action="store_true")
    preview_group.add_argument("--no-preview", dest="preview", action="store_false")
    parser.set_defaults(preview=False)
    parser.add_argument("--udp-port", type=int, default=5055)
    parser.add_argument("--background-seconds", type=float, default=1.0)
    parser.add_argument("--darkening-threshold", type=int, default=28)
    parser.add_argument("--roi", type=parse_roi, default=None, help="Optional normalized ROI: x0,y0,x1,y1")
    parser.add_argument("--roi-circle", type=parse_roi_circle, default=None, help="Optional normalized circle ROI: cx,cy,r")
    parser.add_argument("--roi-ellipse", type=parse_roi_ellipse, default=None, help="Optional normalized ellipse ROI: cx,cy,rx,ry")
    args = parser.parse_args()

    if args.background_seconds < 0.0:
        args.background_seconds = 0.0

    run_tracking(
        args.camera,
        args.udp_port,
        args.background_seconds,
        args.darkening_threshold,
        args.roi,
        args.roi_circle,
        args.roi_ellipse,
        args.width,
        args.height,
        args.fps,
        args.camera_buffer_size,
        args.camera_auto_exposure,
        args.camera_exposure,
        args.camera_autofocus,
        args.directshow_device,
        args.directshow_pixel_format,
        args.directshow_video_codec,
        args.allow_black_frames,
        args.preview,
    )


if __name__ == "__main__":
    main()
