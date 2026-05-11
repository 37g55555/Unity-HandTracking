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


DEFAULT_WIDTH = 320
DEFAULT_HEIGHT = 240
DEFAULT_FPS = 10
DEFAULT_UDP_HOST = os.environ.get("UNITY_UDP_HOST", "127.0.0.1")
DEFAULT_UDP_PORT = int(os.environ.get("UNITY_UDP_PORT", "5053"))
DEFAULT_CAMERA_URL = os.environ.get("IP_CAMERA_URL", "")
DEFAULT_MAX_CAMERA_INDEX = 4
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
MODEL_PATH = Path(__file__).resolve().parent / "hand_landmarker.task"


def log(message):
    print(message, flush=True)


def build_camera_backend_candidates(preferred_backend):
    available_backends = []
    if sys.platform.startswith("win"):
        if hasattr(cv2, "CAP_DSHOW"):
            available_backends.append(("DirectShow", cv2.CAP_DSHOW, "dshow"))
        if hasattr(cv2, "CAP_MSMF"):
            available_backends.append(("MSMF", cv2.CAP_MSMF, "msmf"))
    elif sys.platform == "darwin" and hasattr(cv2, "CAP_AVFOUNDATION"):
        available_backends.append(("AVFoundation", cv2.CAP_AVFOUNDATION, "avfoundation"))

    available_backends.append(("Default", None, "default"))

    if preferred_backend == "auto":
        if sys.platform.startswith("win"):
            stable_windows_backends = [
                (name, backend)
                for name, backend, backend_key in available_backends
                if backend_key == "dshow"
            ]
            if stable_windows_backends:
                return stable_windows_backends

        return [(name, backend) for name, backend, _ in available_backends]

    return [
        (name, backend)
        for name, backend, backend_key in available_backends
        if backend_key == preferred_backend
    ]


def set_camera_properties(cap, width, height, fps):
    if hasattr(cv2, "VideoWriter_fourcc"):
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))

    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    cap.set(cv2.CAP_PROP_FPS, fps)


def frame_luma_stats(frame):
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    return float(gray.mean()), float(gray.std())


def frame_quality_stats(frame):
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    mean_luma = float(gray.mean())
    std_luma = float(gray.std())

    gray_i16 = gray.astype("int16")
    edge_x = float(cv2.absdiff(gray_i16[:, 1:], gray_i16[:, :-1]).mean())
    edge_y = float(cv2.absdiff(gray_i16[1:, :], gray_i16[:-1, :]).mean())

    blue, green, red = cv2.split(frame)
    chroma_rg = cv2.absdiff(red, green).mean()
    chroma_bg = cv2.absdiff(blue, green).mean()
    chroma = float((chroma_rg + chroma_bg) * 0.5)

    return mean_luma, std_luma, edge_x, edge_y, chroma


def is_black_preview_frame(frame):
    mean_luma, std_luma = frame_luma_stats(frame)
    return mean_luma < 8.0 and std_luma < 4.0


def is_corrupt_preview_frame(frame):
    mean_luma, std_luma, edge_x, edge_y, chroma = frame_quality_stats(frame)

    # Some USB webcams return a colorful static pattern when DirectShow/MSMF
    # negotiates a bad stream format while another camera is already open.
    # Real room images can be noisy, but they normally do not have this much
    # per-pixel high-frequency variation and RGB channel disagreement together.
    high_frequency_noise = max(edge_x, edge_y) > 42.0 and (edge_x + edge_y) > 72.0
    color_static = chroma > 38.0
    has_signal = mean_luma > 12.0 and std_luma > 24.0
    return high_frequency_noise and color_static and has_signal


def is_usable_preview_frame(frame):
    return not is_black_preview_frame(frame) and not is_corrupt_preview_frame(frame)


def describe_frame_quality(frame):
    mean_luma, std_luma, edge_x, edge_y, chroma = frame_quality_stats(frame)
    return (
        f"luma_mean={mean_luma:.1f}, luma_std={std_luma:.1f}, "
        f"edge_x={edge_x:.1f}, edge_y={edge_y:.1f}, chroma={chroma:.1f}"
    )


def build_camera_id_candidates(camera_id, allow_fallback, skip_camera_ids, max_camera_index):
    skip_camera_ids = set(skip_camera_ids)
    candidate_ids = []
    if camera_id is not None and camera_id >= 0 and camera_id not in skip_camera_ids:
        candidate_ids.append(camera_id)

    if allow_fallback:
        for fallback_id in range(max_camera_index + 1):
            if fallback_id not in candidate_ids and fallback_id not in skip_camera_ids:
                candidate_ids.append(fallback_id)

    return candidate_ids


def backend_candidates_for_camera(candidate_id, requested_camera_id, backend_candidates, preferred_backend):
    if preferred_backend != "auto":
        return backend_candidates

    if candidate_id == requested_camera_id:
        return backend_candidates

    # DirectShow can block for a long time on missing fallback indices.
    fallback_backends = [(name, backend) for name, backend in backend_candidates if name != "DirectShow"]
    return fallback_backends or backend_candidates


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
    log(f"[INFO] IP camera stream: {resolved_url}")

    cap = cv2.VideoCapture(resolved_url)
    if cap is None or not cap.isOpened():
        if cap is not None:
            cap.release()
        raise SystemExit(f"Could not open IP camera stream: {resolved_url}")

    for _ in range(30):
        ok, frame = cap.read()
        if ok and frame is not None and frame.size > 0:
            log(f"[OK] IP camera connected: {resolved_url}")
            return cap
        time.sleep(0.1)

    cap.release()
    raise SystemExit(f"Could not read frames from IP camera stream: {resolved_url}")


def open_camera(
        camera_id,
        width,
        height,
        fps,
        allow_fallback,
        camera_url,
        preferred_backend,
        skip_camera_ids,
        max_camera_index):
    if camera_url:
        return open_ip_camera(camera_url)

    backend_candidates = build_camera_backend_candidates(preferred_backend)
    if not backend_candidates:
        raise SystemExit(f"Requested camera backend is not available: {preferred_backend}")

    tried = []
    fallback_black_description = ""
    candidate_ids = build_camera_id_candidates(
        camera_id,
        allow_fallback,
        skip_camera_ids,
        max_camera_index)
    log(f"[INFO] Hand camera candidates: {candidate_ids}, backend={preferred_backend}")

    for candidate_id in candidate_ids:
        for backend_name, backend in backend_candidates_for_camera(
                candidate_id,
                camera_id,
                backend_candidates,
                preferred_backend):
            log(f"[INFO] Trying hand camera: camera_id={candidate_id}, backend={backend_name}")
            if backend is None:
                cap = cv2.VideoCapture(candidate_id)
            else:
                cap = cv2.VideoCapture(candidate_id, backend)

            tried.append(f"{candidate_id}:{backend_name}")
            if cap is None or not cap.isOpened():
                if cap is not None:
                    cap.release()
                continue

            set_camera_properties(cap, width, height, fps)

            last_frame = None
            last_quality = ""
            last_status = "unreadable"
            usable_frames = 0
            corrupt_frames = 0
            black_frames = 0
            for _ in range(24):
                ok, frame = cap.read()
                if ok and frame is not None and frame.size > 0:
                    last_frame = frame
                    last_quality = describe_frame_quality(frame)
                    if is_black_preview_frame(frame):
                        black_frames += 1
                        last_status = "black"
                    elif is_corrupt_preview_frame(frame):
                        corrupt_frames += 1
                        last_status = "corrupt"
                    else:
                        usable_frames += 1
                        last_status = "usable"

                    if is_usable_preview_frame(frame):
                        if usable_frames >= 3:
                            log(
                                "[OK] Hand camera connected: "
                                f"camera_id={candidate_id}, backend={backend_name}, "
                                f"{last_quality}"
                            )
                            return cap
                time.sleep(0.05)

            if last_frame is not None:
                description = (
                    f"camera_id={candidate_id}, backend={backend_name}, "
                    f"status={last_status}, usable={usable_frames}, "
                    f"corrupt={corrupt_frames}, black={black_frames}, {last_quality}"
                )
                if corrupt_frames > 0:
                    log(f"[WARN] Hand camera preview looked corrupted; trying another backend/camera: {description}")
                else:
                    log(f"[WARN] Hand camera opened but preview is almost black/unusable: {description}")

                if not fallback_black_description:
                    fallback_black_description = description

            cap.release()
            if last_frame is not None and allow_fallback:
                log(f"[WARN] Trying next camera id after unusable stream from camera_id={candidate_id}.")
                break

    message = (
        "Could not open hand camera. "
        f"Tried: {', '.join(tried)}. "
        "Close any app already using the camera, check Windows camera permissions, "
        "or change --camera."
    )
    if fallback_black_description:
        message += f" Black stream detected: {fallback_black_description}."

    raise SystemExit(message)


def ensure_model_exists():
    if MODEL_PATH.exists():
        return

    log(f"[INFO] Downloading MediaPipe hand model to {MODEL_PATH}")
    urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    log("[OK] MediaPipe hand model downloaded.")


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
    parser = argparse.ArgumentParser(description="MediaPipe hand tracking UDP sender")
    parser.add_argument("--camera", type=int, default=1, help="Hand tracking camera index.")
    parser.add_argument("--camera-url", default=DEFAULT_CAMERA_URL,
                        help="Optional MJPEG/IP camera URL.")
    parser.add_argument("--no-camera-fallback", action="store_true",
                        help="Only use the requested camera index.")
    parser.add_argument("--udp-host", default=DEFAULT_UDP_HOST, help="Unity UDP receiver host.")
    parser.add_argument("--udp-port", type=int, default=DEFAULT_UDP_PORT, help="Unity UDP receiver port.")
    parser.add_argument("--width", type=int, default=DEFAULT_WIDTH, help="Camera frame width.")
    parser.add_argument("--height", type=int, default=DEFAULT_HEIGHT, help="Camera frame height.")
    parser.add_argument("--fps", type=int, default=DEFAULT_FPS, help="Camera FPS.")
    parser.add_argument("--backend", choices=("auto", "dshow", "msmf", "default"), default="auto",
                        help="OpenCV camera backend for local webcams.")
    parser.add_argument("--skip-camera", type=int, action="append", default=[],
                        help="Camera index reserved for another process. Can be used multiple times.")
    parser.add_argument("--max-camera-index", type=int, default=DEFAULT_MAX_CAMERA_INDEX,
                        help="Highest fallback camera index to probe.")
    parser.add_argument("--retry-forever", action="store_true",
                        help="Keep retrying camera open instead of exiting when no usable camera is available.")
    parser.add_argument("--retry-interval", type=float, default=3.0,
                        help="Seconds to wait between camera open retries.")
    parser.add_argument("--packet-width", type=int, default=DEFAULT_WIDTH,
                        help="Canonical coordinate width sent to Unity.")
    parser.add_argument("--packet-height", type=int, default=DEFAULT_HEIGHT,
                        help="Canonical coordinate height sent to Unity.")
    parser.add_argument("--no-mirror", action="store_true",
                        help="Do not horizontally mirror the camera image.")
    args = parser.parse_args()

    ensure_model_exists()

    while True:
        try:
            cap = open_camera(
                args.camera,
                args.width,
                args.height,
                args.fps,
                not args.no_camera_fallback,
                args.camera_url,
                args.backend,
                args.skip_camera,
                args.max_camera_index)
            break
        except SystemExit as exc:
            if not args.retry_forever:
                raise

            retry_interval = max(args.retry_interval, 0.5)
            log(f"[WARN] {exc}. Retrying in {retry_interval:.1f}s...")
            time.sleep(retry_interval)

    udp_target = (args.udp_host, args.udp_port)
    log(f"[INFO] Sending landmarks to Unity UDP {udp_target[0]}:{udp_target[1]}")
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    landmarker = create_landmarker()
    last_timestamp_ms = 0
    bad_runtime_frames = 0

    try:
        while True:
            success, frame = cap.read()
            if not success or frame is None:
                continue

            if is_black_preview_frame(frame) or is_corrupt_preview_frame(frame):
                bad_runtime_frames += 1
                if bad_runtime_frames == 1 or bad_runtime_frames % 60 == 0:
                    log(f"[WARN] Dropping unusable hand camera frame: {describe_frame_quality(frame)}")
                continue

            bad_runtime_frames = 0

            if not args.no_mirror:
                frame = cv2.flip(frame, 1)

            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
            timestamp_ms = int(time.perf_counter() * 1000)
            if timestamp_ms <= last_timestamp_ms:
                timestamp_ms = last_timestamp_ms + 1
            last_timestamp_ms = timestamp_ms

            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            if result.hand_landmarks:
                payload = build_udp_payload(result, args.packet_width, args.packet_height)
                sock.sendto(str(payload).encode("utf-8"), udp_target)
                for hand_landmarks in result.hand_landmarks:
                    draw_landmarks(frame, hand_landmarks)

            cv2.imshow("MediaPipe Hand Tracking", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
    finally:
        landmarker.close()
        sock.close()
        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
