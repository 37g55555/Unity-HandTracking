import sys

import cv2


DEFAULT_CAMERA_WIDTH = 1920
DEFAULT_CAMERA_HEIGHT = 1080
DEFAULT_CAMERA_FPS = 30


def get_camera_backend():
    if sys.platform.startswith("win") and hasattr(cv2, "CAP_DSHOW"):
        return cv2.CAP_DSHOW

    if sys.platform == "darwin" and hasattr(cv2, "CAP_AVFOUNDATION"):
        return cv2.CAP_AVFOUNDATION

    return None


def open_camera(
    camera_id,
    width=DEFAULT_CAMERA_WIDTH,
    height=DEFAULT_CAMERA_HEIGHT,
    fps=DEFAULT_CAMERA_FPS,
    buffer_size=1,
    log=None,
):
    backend = get_camera_backend()
    cap = cv2.VideoCapture(camera_id, backend) if backend is not None else cv2.VideoCapture(camera_id)

    if not cap.isOpened():
        raise SystemExit(f"Camera {camera_id} could not be opened.")

    if hasattr(cv2, "VideoWriter_fourcc"):
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))

    cap.set(cv2.CAP_PROP_BUFFERSIZE, buffer_size)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    cap.set(cv2.CAP_PROP_FPS, fps)

    ok, frame = cap.read()
    if not ok or frame is None or frame.size == 0:
        cap.release()
        raise SystemExit(f"Camera {camera_id} did not return a valid frame.")

    message = f"[OK] Camera {camera_id} ready."
    if log is None:
        print(message, flush=True)
    else:
        log(message)

    return cap
