import argparse
import socket
import time

import cv2
import numpy as np

from camera_utils import open_camera
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


def build_roi_mask(frame_shape, roi):
    if roi is None:
        return None

    height, width = frame_shape[:2]
    x0, y0, x1, y1 = roi
    x0 = int(np.clip(round(x0 * width), 0, width - 1))
    x1 = int(np.clip(round(x1 * width), x0 + 1, width))
    y0 = int(np.clip(round(y0 * height), 0, height - 1))
    y1 = int(np.clip(round(y1 * height), y0 + 1, height))

    mask = np.zeros((height, width), dtype=np.uint8)
    mask[y0:y1, x0:x1] = 255
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


def capture_background(cap, seconds, roi_mask):
    started_at = time.monotonic()
    frames = []
    restore_focus_window = get_foreground_window()
    preview_focus_restored = False

    log(f"[INFO] Capturing empty background for {seconds:.1f}s.")
    cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
    configure_preview_window(cv2, PREVIEW_WINDOW_NAME, restore_focus_window)

    while True:
        ok, frame = cap.read()
        if not ok or frame is None:
            continue

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        frames.append(gray.astype(np.float32))

        display = frame.copy()
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


def calculate_shadow_ratio(gray, background, roi_mask, darkening_threshold, black_threshold):
    if background is not None:
        shadow_mask = (background - gray.astype(np.float32)) >= darkening_threshold
    else:
        shadow_mask = gray <= black_threshold

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
    black_threshold,
    use_background,
    roi,
):
    cap = open_camera(camera_id, log=log)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    udp_target = (UDP_HOST, udp_port)
    roi_mask = None
    background = None

    try:
        ok, first_frame = cap.read()
        if ok and first_frame is not None:
            roi_mask = build_roi_mask(first_frame.shape, roi)

        if use_background:
            background = capture_background(cap, background_seconds, roi_mask)

        restore_focus_window = get_foreground_window()
        preview_focus_restored = False
        cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
        configure_preview_window(cv2, PREVIEW_WINDOW_NAME, restore_focus_window)
        log(f"[OK] Sending shadow ratio to Unity UDP {UDP_HOST}:{udp_port}.")

        while True:
            ok, frame = cap.read()
            if not ok or frame is None:
                continue

            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            ratio, mask = calculate_shadow_ratio(
                gray,
                background,
                roi_mask,
                darkening_threshold,
                black_threshold,
            )
            payload = f"ratio={ratio:.6f}"
            sock.sendto(payload.encode("utf-8"), udp_target)

            display = draw_shadow_overlay(frame.copy(), mask)
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
    parser.add_argument("--udp-port", type=int, default=5055)
    parser.add_argument("--background-seconds", type=float, default=1.0)
    parser.add_argument("--darkening-threshold", type=int, default=28)
    parser.add_argument("--black-threshold", type=int, default=70)
    parser.add_argument("--use-background", dest="use_background", action="store_true")
    parser.add_argument("--no-background", dest="use_background", action="store_false")
    parser.add_argument("--roi", type=parse_roi, default=None, help="Optional normalized ROI: x0,y0,x1,y1")
    parser.set_defaults(use_background=True)
    args = parser.parse_args()

    if args.background_seconds < 0.0:
        args.background_seconds = 0.0

    run_tracking(
        args.camera,
        args.udp_port,
        args.background_seconds,
        args.darkening_threshold,
        args.black_threshold,
        args.use_background,
        args.roi,
    )


if __name__ == "__main__":
    main()
