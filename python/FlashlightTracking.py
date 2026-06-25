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
PREVIEW_WINDOW_NAME = "Flashlight Tracking"
QUIT_KEY = ord("q")


def log(message):
    print(message, flush=True)


def capture_background(cap, seconds, show_preview):
    started_at = time.monotonic()
    frames = []
    log(f"[INFO] Capturing flashlight background for {seconds:.1f}s.")

    while True:
        success, frame = cap.read(copy_frame=show_preview)
        if not success or frame is None:
            continue

        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        frames.append(hsv[:, :, 2].astype(np.float32))

        if show_preview:
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
            cv2.imshow(PREVIEW_WINDOW_NAME, display)
            if cv2.waitKey(1) & 0xFF == QUIT_KEY:
                raise KeyboardInterrupt

        if time.monotonic() - started_at >= seconds and frames:
            break

    background = np.mean(frames, axis=0).astype(np.float32)
    log(f"[OK] Flashlight background captured from {len(frames)} frame(s).")
    return background


def find_brightest_white_blob(
    frame,
    threshold,
    max_saturation,
    min_area,
    max_area_ratio,
    background_value,
    brightening_threshold=45,
):
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    saturation = hsv[:, :, 1]
    value = hsv[:, :, 2]

    mask = np.zeros_like(value, dtype=np.uint8)
    if background_value is not None and background_value.shape == value.shape:
        brightened = (value.astype(np.float32) - background_value) >= float(brightening_threshold)
        bright_enough = value >= int(threshold)
        saturation_ok = saturation <= int(max_saturation)
        mask[brightened & bright_enough & saturation_ok] = 255

    kernel = np.ones((5, 5), np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    mask = cv2.dilate(mask, kernel, iterations=1)

    contours, _hierarchy = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None, mask

    height, width = frame.shape[:2]
    max_area = max(1.0, float(width * height) * max_area_ratio)
    candidates = []
    for contour in contours:
        area = float(cv2.contourArea(contour))
        if min_area <= area <= max_area:
            candidates.append((area, contour))

    if not candidates:
        return None, mask

    _area, contour = max(candidates, key=lambda item: item[0])
    moments = cv2.moments(contour)
    if moments["m00"] == 0:
        return None, mask

    center_x = float(moments["m10"] / moments["m00"])
    center_y = float(moments["m01"] / moments["m00"])
    radius = float(cv2.minEnclosingCircle(contour)[1])
    return (center_x, center_y, _area, radius), mask


def run_tracking(
    camera_id,
    udp_port,
    width,
    height,
    fps,
    camera_buffer_size,
    threshold,
    max_saturation,
    min_area,
    max_area_ratio,
    background_seconds,
    brightening_threshold,
    show_preview,
):
    cap = open_latest_frame_camera(
        camera_id,
        width=width,
        height=height,
        fps=fps,
        buffer_size=camera_buffer_size,
        allow_black_frames=True,
        log=log,
    )
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    udp_target = (UDP_HOST, udp_port)
    restore_focus_window = get_foreground_window()
    preview_focus_restored = False

    log(f"[OK] Tracking flashlight highlight on camera {camera_id}.")
    log(f"[OK] Sending light point to Unity UDP {UDP_HOST}:{udp_port}.")

    if show_preview:
        cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
        configure_preview_window(cv2, PREVIEW_WINDOW_NAME, restore_focus_window)

    background_value = capture_background(cap, max(0.0, background_seconds), show_preview)

    try:
        while True:
            success, frame = cap.read(copy_frame=show_preview)
            if not success or frame is None:
                continue

            detection, mask = find_brightest_white_blob(
                frame,
                threshold,
                max_saturation,
                min_area,
                max_area_ratio,
                background_value,
                brightening_threshold,
            )

            if detection is not None:
                center_x, center_y, area, radius = detection
                frame_height, frame_width = frame.shape[:2]
                viewport_x = center_x / max(1.0, frame_width - 1.0)
                viewport_y = 1.0 - (center_y / max(1.0, frame_height - 1.0))
                payload = f"FLASHLIGHT,{viewport_x:.6f},{viewport_y:.6f},{area:.1f}"
                sock.sendto(payload.encode("utf-8"), udp_target)

                if show_preview:
                    cv2.circle(frame, (int(center_x), int(center_y)), int(max(8.0, radius)), (0, 180, 255), 2)
                    cv2.circle(frame, (int(center_x), int(center_y)), 4, (255, 255, 255), -1)

            if show_preview:
                cv2.imshow(PREVIEW_WINDOW_NAME, frame)
                cv2.imshow(f"{PREVIEW_WINDOW_NAME} Mask", mask)
                if not preview_focus_restored:
                    keep_preview_window_no_activate(PREVIEW_WINDOW_NAME, restore_focus_window)
                    preview_focus_restored = True

                if cv2.waitKey(1) & 0xFF == QUIT_KEY:
                    break
    finally:
        sock.close()
        cap.release()
        if show_preview:
            cv2.destroyWindow(PREVIEW_WINDOW_NAME)
            cv2.destroyWindow(f"{PREVIEW_WINDOW_NAME} Mask")


def main():
    parser = argparse.ArgumentParser(description="Track a bright white flashlight spot and send its screen pose to Unity.")
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--udp-port", type=int, default=5056)
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--fps", type=int, default=120)
    parser.add_argument("--camera-buffer-size", type=int, default=1)
    parser.add_argument("--threshold", type=int, default=245)
    parser.add_argument("--max-saturation", type=int, default=120)
    parser.add_argument("--min-area", type=float, default=120.0)
    parser.add_argument("--max-area-ratio", type=float, default=0.2)
    parser.add_argument("--background-seconds", type=float, default=1.0)
    parser.add_argument("--brightening-threshold", type=int, default=45)
    parser.add_argument("--show", action="store_true")
    args = parser.parse_args()

    run_tracking(
        camera_id=args.camera,
        udp_port=args.udp_port,
        width=args.width,
        height=args.height,
        fps=args.fps,
        camera_buffer_size=args.camera_buffer_size,
        threshold=args.threshold,
        max_saturation=args.max_saturation,
        min_area=args.min_area,
        max_area_ratio=args.max_area_ratio,
        background_seconds=args.background_seconds,
        brightening_threshold=args.brightening_threshold,
        show_preview=args.show,
    )


if __name__ == "__main__":
    main()
