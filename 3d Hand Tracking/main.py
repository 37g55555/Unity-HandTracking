from pathlib import Path
import argparse
import os
import socket
import sys
import time
import urllib.request
from urllib.parse import urlparse

import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision

# Parameters
WIDTH, HEIGHT = 1280, 720
DEFAULT_CAMERA_URL = os.environ.get("IP_CAMERA_URL", "")
DEFAULT_UDP_HOST = os.environ.get("UNITY_UDP_HOST", "127.0.0.1")
DEFAULT_UDP_PORT = int(os.environ.get("UNITY_UDP_PORT", "5052"))
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
MODEL_PATH = Path(__file__).resolve().parent / "hand_landmarker.task"


def build_camera_backend_candidates():
    backend_candidates = []
    if sys.platform.startswith("win"):
        if hasattr(cv2, "CAP_DSHOW"):
            backend_candidates.append(("DirectShow", cv2.CAP_DSHOW))
        if hasattr(cv2, "CAP_MSMF"):
            backend_candidates.append(("MSMF", cv2.CAP_MSMF))
    elif sys.platform == "darwin" and hasattr(cv2, "CAP_AVFOUNDATION"):
        backend_candidates.append(("AVFoundation", cv2.CAP_AVFOUNDATION))

    backend_candidates.append(("Default", None))
    return backend_candidates


def build_camera_id_candidates(camera_id=1, allow_fallback=True):
    candidate_ids = []
    if camera_id is not None and camera_id >= 0:
        candidate_ids.append(camera_id)

    if allow_fallback:
        for fallback_id in range(6):
            if fallback_id not in candidate_ids:
                candidate_ids.append(fallback_id)

    return candidate_ids


def normalize_camera_url(camera_url):
    if not camera_url:
        return ""

    camera_url = camera_url.strip()
    if "://" not in camera_url:
        camera_url = f"http://{camera_url}"

    parsed = urlparse(camera_url)
    if parsed.path in ("", "/"):
        camera_url = camera_url.rstrip("/") + "/video"

    return camera_url


def open_ip_camera(camera_url):
    resolved_url = normalize_camera_url(camera_url)
    print(f"[INFO] IP camera stream: {resolved_url}")

    cap = cv2.VideoCapture(resolved_url)
    if cap is None or not cap.isOpened():
        if cap is not None:
            cap.release()
        raise SystemExit(f"Could not open IP camera stream: {resolved_url}")

    for _ in range(30):
        ok, frame = cap.read()
        if ok and frame is not None and frame.size > 0:
            print(f"[OK] IP camera connected: {resolved_url}")
            return cap
        time.sleep(0.1)

    cap.release()
    raise SystemExit(f"Could not read frames from IP camera stream: {resolved_url}")


def open_camera(camera_id=1, allow_fallback=True, camera_url=""):
    if camera_url:
        return open_ip_camera(camera_url)

    candidate_ids = build_camera_id_candidates(camera_id, allow_fallback)
    backend_candidates = build_camera_backend_candidates()

    tried = []
    for candidate_id in candidate_ids:
        for backend_name, backend in backend_candidates:
            try:
                if backend is None:
                    cap = cv2.VideoCapture(candidate_id)
                else:
                    cap = cv2.VideoCapture(candidate_id, backend)
            except Exception:
                cap = None

            tried.append(f"{candidate_id}:{backend_name}")
            if cap is None or not cap.isOpened():
                if cap is not None:
                    cap.release()
                continue

            cap.set(cv2.CAP_PROP_FRAME_WIDTH, WIDTH)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, HEIGHT)

            success = False
            for _ in range(12):
                ok, frame = cap.read()
                if ok and frame is not None and frame.size > 0:
                    success = True
                    break
                time.sleep(0.05)

            if success:
                print(f"[OK] Camera connected: camera_id={candidate_id}, backend={backend_name}")
                return cap

            cap.release()

    raise SystemExit(
        "Could not open a usable camera. "
        f"Tried: {', '.join(tried)}. "
        "Check Windows camera permissions, close any app already using the camera, "
        "or pass --camera-url for the iOS IP camera MJPEG stream."
    )


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


def build_udp_payload(result, packet_width, packet_height):
    data = []

    for hand_landmarks in result.hand_landmarks:
        for landmark in hand_landmarks:
            x = landmark.x * packet_width
            y = (1.0 - landmark.y) * packet_height
            z = landmark.z * packet_width
            data.extend([round(x, 3), round(y, 3), round(z, 5)])

    return data


def main():
    parser = argparse.ArgumentParser(description="MediaPipe hand tracking sender")
    parser.add_argument("--camera", type=int, default=1, help="Preferred camera index")
    parser.add_argument("--camera-url", type=str, default=DEFAULT_CAMERA_URL,
                        help="MJPEG/IP camera URL. Example: http://192.168.0.12:8081/video")
    parser.add_argument("--no-camera-fallback", action="store_true",
                        help="Only use the requested camera index. Recommended for two-webcam installations.")
    parser.add_argument("--udp-host", type=str, default=DEFAULT_UDP_HOST,
                        help="Unity UDP receiver host.")
    parser.add_argument("--udp-port", type=int, default=DEFAULT_UDP_PORT,
                        help="Unity UDP receiver port.")
    parser.add_argument("--packet-width", type=int, default=WIDTH,
                        help="Canonical coordinate width sent to Unity.")
    parser.add_argument("--packet-height", type=int, default=HEIGHT,
                        help="Canonical coordinate height sent to Unity.")
    parser.add_argument("--no-mirror", action="store_true",
                        help="Do not horizontally mirror the camera frame before MediaPipe.")
    args = parser.parse_args()

    ensure_model_exists()

    cap = open_camera(
        args.camera,
        allow_fallback=not args.no_camera_fallback,
        camera_url=args.camera_url)

    udp_target = (args.udp_host, args.udp_port)
    print(f"[INFO] Sending landmarks to Unity UDP {udp_target[0]}:{udp_target[1]}")
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    landmarker = create_landmarker()

    try:
        while True:
            success, frame = cap.read()
            if not success:
                continue

            if not args.no_mirror:
                frame = cv2.flip(frame, 1)
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
            timestamp_ms = int(time.perf_counter() * 1000)

            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            if result.hand_landmarks:
                payload = build_udp_payload(result, args.packet_width, args.packet_height)
                sock.sendto(str(payload).encode("utf-8"), udp_target)

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
