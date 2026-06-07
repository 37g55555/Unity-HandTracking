from pathlib import Path
import argparse
import socket
import time

import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision

CAMERA_WIDTH = 1920
CAMERA_HEIGHT = 1080
CAMERA_FPS = 30
PACKET_WIDTH = 1920
PACKET_HEIGHT = 1080
UDP_HOST = "127.0.0.1"
UDP_PORT = 5053
MODEL_PATH = Path(__file__).resolve().parent / "MediaPipe.task"


def log(message):
    print(message, flush=True)


def ensure_model_exists():
    if not MODEL_PATH.exists():
        raise SystemExit(f"MediaPipe model was not found: {MODEL_PATH}")


def open_camera(camera_id):
    backend = cv2.CAP_DSHOW if hasattr(cv2, "CAP_DSHOW") else None
    cap = cv2.VideoCapture(camera_id, backend) if backend is not None else cv2.VideoCapture(camera_id)

    if not cap.isOpened():
        raise SystemExit(f"Camera {camera_id} could not be opened.")

    if hasattr(cv2, "VideoWriter_fourcc"):
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))

    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, CAMERA_WIDTH)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAMERA_HEIGHT)
    cap.set(cv2.CAP_PROP_FPS, CAMERA_FPS)

    ok, frame = cap.read()
    if not ok or frame is None or frame.size == 0:
        cap.release()
        raise SystemExit(f"Camera {camera_id} did not return a valid frame.")

    log(f"[OK] Camera {camera_id} ready.")
    return cap


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


def run_tracking(camera_id):
    ensure_model_exists()

    cap = open_camera(camera_id)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    landmarker = create_landmarker()
    udp_target = (UDP_HOST, UDP_PORT)
    last_timestamp_ms = 0

    log(f"[OK] Sending landmarks to Unity UDP {UDP_HOST}:{UDP_PORT}.")

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
            if result.hand_landmarks:
                payload = build_udp_payload(result)
                sock.sendto(str(payload).encode("utf-8"), udp_target)
    finally:
        landmarker.close()
        sock.close()
        cap.release()


def main():
    parser = argparse.ArgumentParser(description="Send MediaPipe hand landmarks to Unity.")
    parser.add_argument("--camera", type=int, default=1)
    args = parser.parse_args()

    run_tracking(args.camera)


if __name__ == "__main__":
    main()
