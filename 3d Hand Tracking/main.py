from pathlib import Path
import socket
import time
import urllib.request

import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision

# Parameters
WIDTH, HEIGHT = 1280, 720
UDP_TARGET = ("127.0.0.1", 5052)
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
MODEL_PATH = Path(__file__).resolve().parent / "hand_landmarker.task"


def ensure_model_exists():
    if MODEL_PATH.exists():
        return

    print(f"Downloading MediaPipe hand model to {MODEL_PATH} ...")
    urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    print("Model download complete.")


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


def draw_landmarks(frame, hand_landmarks):
    frame_height, frame_width = frame.shape[:2]

    for landmark in hand_landmarks:
        px = int(landmark.x * frame_width)
        py = int(landmark.y * frame_height)
        cv2.circle(frame, (px, py), 4, (0, 255, 0), cv2.FILLED)


def build_udp_payload(result, frame_width, frame_height):
    data = []

    for hand_landmarks in result.hand_landmarks:
        for landmark in hand_landmarks:
            x = landmark.x * frame_width
            y = (1.0 - landmark.y) * frame_height
            z = landmark.z * frame_width
            data.extend([round(x, 3), round(y, 3), round(z, 5)])

    return data


def main():
    ensure_model_exists()

    cap = cv2.VideoCapture(0)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, WIDTH)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, HEIGHT)

    if not cap.isOpened():
        raise SystemExit("Could not open camera 0. Check macOS camera permissions.")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    landmarker = create_landmarker()

    try:
        while True:
            success, frame = cap.read()
            if not success:
                continue

            frame = cv2.flip(frame, 1)
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
            timestamp_ms = int(time.perf_counter() * 1000)

            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            if result.hand_landmarks:
                payload = build_udp_payload(result, frame.shape[1], frame.shape[0])
                sock.sendto(str(payload).encode("utf-8"), UDP_TARGET)

                for hand_landmarks in result.hand_landmarks:
                    draw_landmarks(frame, hand_landmarks)

            cv2.imshow("Image", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
    finally:
        landmarker.close()
        sock.close()
        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
