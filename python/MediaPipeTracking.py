from pathlib import Path
import argparse
import socket
import time

import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision

from preview_window_utils import (
    configure_preview_window,
    get_foreground_window,
    keep_preview_window_no_activate,
)
from camera_utils import open_camera

PACKET_WIDTH = 1920
PACKET_HEIGHT = 1080
UDP_HOST = "127.0.0.1"
UDP_PORT = 5053
MODEL_PATH = Path(__file__).resolve().parent / "MediaPipe.task"
PREVIEW_WINDOW_NAME = "Hand Tracking"
QUIT_KEY = ord("q")

def log(message):
    print(message, flush=True)


def ensure_model_exists():
    if not MODEL_PATH.exists():
        raise SystemExit(f"MediaPipe model was not found: {MODEL_PATH}")


def create_landmarker():
    base_options = python.BaseOptions(model_asset_path=str(MODEL_PATH))
    options = vision.HandLandmarkerOptions(
        base_options=base_options,
        running_mode=vision.RunningMode.VIDEO,
        num_hands=2,
        min_hand_detection_confidence=0.5,
        min_hand_presence_confidence=0.5,
        min_tracking_confidence=0.5,
    )
    return vision.HandLandmarker.create_from_options(options)


def build_udp_payload(result):
    data = []
    for hand_landmarks in result.hand_landmarks:
        for landmark in hand_landmarks:
            x = landmark.x * PACKET_WIDTH
            y = (1.0 - landmark.y) * PACKET_HEIGHT
            z = landmark.z * PACKET_WIDTH
            data.extend([round(x, 3), round(y, 3), round(z, 5)])
    return data


def draw_hand_landmarks(display, result):
    if not result.hand_landmarks:
        return

    height, width = display.shape[:2]
    for hand_landmarks in result.hand_landmarks:
        points = []
        for landmark in hand_landmarks:
            x = int(max(0.0, min(1.0, landmark.x)) * (width - 1))
            y = int(max(0.0, min(1.0, landmark.y)) * (height - 1))
            points.append((x, y))

        for point in points:
            cv2.circle(display, point, 4, (0, 255, 0), -1)


def run_tracking(camera_id):
    ensure_model_exists()

    cap = open_camera(camera_id, log=log)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    landmarker = create_landmarker()
    udp_target = (UDP_HOST, UDP_PORT)
    last_timestamp_ms = 0
    restore_focus_window = get_foreground_window()
    preview_focus_restored = False

    log(f"[OK] Sending landmarks to Unity UDP {UDP_HOST}:{UDP_PORT}.")
    cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
    configure_preview_window(cv2, PREVIEW_WINDOW_NAME, restore_focus_window)

    try:
        while True:
            success, frame = cap.read()
            if not success or frame is None:
                continue

            frame = cv2.flip(frame, 1)
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)

            timestamp_ms = int(time.perf_counter() * 1000)
            if timestamp_ms <= last_timestamp_ms:
                timestamp_ms = last_timestamp_ms + 1
            last_timestamp_ms = timestamp_ms

            result = landmarker.detect_for_video(mp_image, timestamp_ms)
            hand_count = len(result.hand_landmarks) if result.hand_landmarks else 0
            if hand_count > 0:
                payload = build_udp_payload(result)
                sock.sendto(str(payload).encode("utf-8"), udp_target)

            draw_hand_landmarks(frame, result)
            cv2.imshow(PREVIEW_WINDOW_NAME, frame)
            if not preview_focus_restored:
                keep_preview_window_no_activate(PREVIEW_WINDOW_NAME, restore_focus_window)
                preview_focus_restored = True

            if cv2.waitKey(1) & 0xFF == QUIT_KEY:
                break
    finally:
        landmarker.close()
        sock.close()
        cap.release()
        cv2.destroyWindow(PREVIEW_WINDOW_NAME)


def main():
    parser = argparse.ArgumentParser(description="Send MediaPipe hand landmarks to Unity.")
    parser.add_argument("--camera", type=int, default=1)
    args = parser.parse_args()

    run_tracking(args.camera)


if __name__ == "__main__":
    main()
