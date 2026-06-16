import sys
import threading
import time

import cv2


DEFAULT_CAMERA_WIDTH = 640
DEFAULT_CAMERA_HEIGHT = 360
DEFAULT_CAMERA_FPS = 30
DEFAULT_CAMERA_BUFFER_SIZE = 1
DEFAULT_CAMERA_AUTO_EXPOSURE = 0.75
DEFAULT_CAMERA_EXPOSURE = None
DEFAULT_CAMERA_AUTOFOCUS = 0.0
DEFAULT_CAMERA_BRIGHTNESS = None
DEFAULT_CAMERA_GAIN = None
DEFAULT_CAMERA_CONTRAST = None
DEFAULT_CAMERA_NO_FRAME_REOPEN_SECONDS = 1.0
DEFAULT_CAMERA_BLACK_FRAME_REOPEN_SECONDS = 1.2
DEFAULT_CAMERA_BLACK_FRAME_MEAN_THRESHOLD = 3.0
DEFAULT_CAMERA_REOPEN_BACKOFF_SECONDS = 0.35
DEFAULT_CAMERA_ALLOW_BLACK_FRAMES = False
DEFAULT_DIRECTSHOW_DEVICE = ""
DEFAULT_DIRECTSHOW_PIXEL_FORMAT = ""
DEFAULT_DIRECTSHOW_VIDEO_CODEC = ""
DEFAULT_DIRECTSHOW_OPEN_ATTEMPTS = 5
DEFAULT_DIRECTSHOW_RETRY_SECONDS = 0.75


def emit_log(log, message):
    if log is None:
        print(message, flush=True)
    else:
        log(message)


def configure_opencv_runtime():
    try:
        cv2.setUseOptimized(True)
    except cv2.error:
        pass

    try:
        cv2.setNumThreads(1)
    except cv2.error:
        pass


def get_camera_backend_candidates():
    candidates = []

    if sys.platform.startswith("win"):
        if hasattr(cv2, "CAP_DSHOW"):
            candidates.append(("DirectShow", cv2.CAP_DSHOW))
        if hasattr(cv2, "CAP_MSMF"):
            candidates.append(("Media Foundation", cv2.CAP_MSMF))
        candidates.append(("default", None))
        return candidates

    if sys.platform == "darwin" and hasattr(cv2, "CAP_AVFOUNDATION"):
        candidates.append(("AVFoundation", cv2.CAP_AVFOUNDATION))

    candidates.append(("default", None))
    return candidates


def set_capture_property(cap, property_id, value, label, log=None):
    if value is None:
        return

    try:
        requested = float(value)
    except (TypeError, ValueError):
        emit_log(log, f"[WARN] Camera {label} value is invalid: {value}")
        return

    try:
        success = cap.set(property_id, requested)
        actual = cap.get(property_id)
    except cv2.error as exception:
        emit_log(log, f"[WARN] Camera {label} could not be set: {exception}")
        return

    if not success:
        emit_log(log, f"[WARN] Camera {label}: requested {requested:g}, actual {actual:g}")


def create_capture(camera_id, log=None):
    last_capture = None
    for backend_name, backend in get_camera_backend_candidates():
        cap = cv2.VideoCapture(camera_id, backend) if backend is not None else cv2.VideoCapture(camera_id)
        if cap.isOpened():
            emit_log(log, f"[INFO] Camera {camera_id} opened with {backend_name}.")
            return cap

        cap.release()
        last_capture = cap

    if last_capture is not None:
        last_capture.release()
    raise SystemExit(f"Camera {camera_id} could not be opened.")


class PyAvDirectShowCamera:
    def __init__(
        self,
        device_name,
        width=DEFAULT_CAMERA_WIDTH,
        height=DEFAULT_CAMERA_HEIGHT,
        fps=DEFAULT_CAMERA_FPS,
        pixel_format=DEFAULT_DIRECTSHOW_PIXEL_FORMAT,
        video_codec=DEFAULT_DIRECTSHOW_VIDEO_CODEC,
        log=None,
    ):
        try:
            import av
        except ImportError as exception:
            raise SystemExit("PyAV is required for --directshow-device capture.") from exception

        self._device_name = str(device_name)
        self._log = log
        self._container = None
        self._packet_iterator = None
        self._opened = False
        self._last_shape = None
        self._fps = fps

        options = {
            "video_size": f"{int(width)}x{int(height)}",
            "framerate": str(int(fps)),
        }

        if pixel_format:
            options["pixel_format"] = str(pixel_format)

        if video_codec:
            options["vcodec"] = str(video_codec)

        emit_log(log, f"[INFO] Opening DirectShow device {self._device_name}.")
        self._container = av.open(f"video={self._device_name}", format="dshow", options=options)
        self._packet_iterator = self._container.demux(video=0)
        self._opened = True

    def read(self):
        if not self._opened or self._packet_iterator is None:
            return False, None

        try:
            for packet in self._packet_iterator:
                for frame in packet.decode():
                    array = frame.to_ndarray(format="bgr24")
                    self._last_shape = array.shape
                    return True, array
        except Exception as exception:
            emit_log(self._log, f"[WARN] DirectShow read failed: {exception}")
            return False, None

        return False, None

    def release(self):
        self._opened = False
        if self._container is not None:
            try:
                self._container.close()
            except Exception:
                pass
            self._container = None
        self._packet_iterator = None

    def isOpened(self):
        return self._opened

    def get(self, property_id):
        if property_id == cv2.CAP_PROP_FRAME_WIDTH and self._last_shape is not None:
            return float(self._last_shape[1])

        if property_id == cv2.CAP_PROP_FRAME_HEIGHT and self._last_shape is not None:
            return float(self._last_shape[0])

        if property_id == cv2.CAP_PROP_FPS:
            return float(self._fps)

        return 0.0


def read_valid_frame(cap, timeout=1.0):
    deadline = time.perf_counter() + max(0.0, timeout)
    while time.perf_counter() <= deadline:
        ok, frame = cap.read()
        if ok and frame is not None and frame.size > 0:
            return True, frame
        time.sleep(0.02)

    return False, None


def is_nearly_black_frame(frame, mean_threshold=DEFAULT_CAMERA_BLACK_FRAME_MEAN_THRESHOLD):
    if frame is None or frame.size <= 0:
        return False

    return float(frame.mean()) <= float(mean_threshold)


def open_camera(
    camera_id,
    fallback_camera_ids=None,
    width=DEFAULT_CAMERA_WIDTH,
    height=DEFAULT_CAMERA_HEIGHT,
    fps=DEFAULT_CAMERA_FPS,
    buffer_size=DEFAULT_CAMERA_BUFFER_SIZE,
    auto_exposure=DEFAULT_CAMERA_AUTO_EXPOSURE,
    exposure=DEFAULT_CAMERA_EXPOSURE,
    autofocus=DEFAULT_CAMERA_AUTOFOCUS,
    brightness=DEFAULT_CAMERA_BRIGHTNESS,
    gain=DEFAULT_CAMERA_GAIN,
    contrast=DEFAULT_CAMERA_CONTRAST,
    allow_black_frames=DEFAULT_CAMERA_ALLOW_BLACK_FRAMES,
    directshow_device=DEFAULT_DIRECTSHOW_DEVICE,
    directshow_pixel_format=DEFAULT_DIRECTSHOW_PIXEL_FORMAT,
    directshow_video_codec=DEFAULT_DIRECTSHOW_VIDEO_CODEC,
    log=None,
):
    configure_opencv_runtime()

    if directshow_device:
        last_error = None
        for attempt in range(1, DEFAULT_DIRECTSHOW_OPEN_ATTEMPTS + 1):
            cap = None
            try:
                cap = PyAvDirectShowCamera(
                    directshow_device,
                    width=width,
                    height=height,
                    fps=fps,
                    pixel_format=directshow_pixel_format,
                    video_codec=directshow_video_codec,
                    log=log,
                )

                ok, frame = read_valid_frame(cap)
                if not ok or frame is None or frame.size == 0:
                    raise SystemExit(f"DirectShow device did not return a valid frame: {directshow_device}")

                if is_nearly_black_frame(frame) and not allow_black_frames:
                    raise SystemExit(f"DirectShow device returned a nearly black frame: {directshow_device}")

                actual_height, actual_width = frame.shape[:2]
                emit_log(log, f"[OK] DirectShow device ready at {actual_width}x{actual_height} @ {float(fps):.1f} fps.")
                return cap
            except (Exception, SystemExit) as exception:
                last_error = exception
                if cap is not None:
                    cap.release()
                if attempt < DEFAULT_DIRECTSHOW_OPEN_ATTEMPTS:
                    emit_log(log, f"[WARN] DirectShow open attempt {attempt} failed: {exception}")
                    time.sleep(DEFAULT_DIRECTSHOW_RETRY_SECONDS)

        if last_error is not None:
            raise last_error

        raise SystemExit(f"DirectShow device could not be opened: {directshow_device}")

    camera_candidates = [int(camera_id)]
    if fallback_camera_ids:
        for fallback_id in fallback_camera_ids:
            fallback_id = int(fallback_id)
            if fallback_id not in camera_candidates:
                camera_candidates.append(fallback_id)

    last_error = None
    for candidate_id in camera_candidates:
        try:
            cap = create_capture(candidate_id, log=log)
        except SystemExit as exception:
            last_error = exception
            continue

        if hasattr(cv2, "VideoWriter_fourcc"):
            cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))

        set_capture_property(cap, cv2.CAP_PROP_BUFFERSIZE, buffer_size, "buffer size", log)
        set_capture_property(cap, cv2.CAP_PROP_FRAME_WIDTH, width, "width", log)
        set_capture_property(cap, cv2.CAP_PROP_FRAME_HEIGHT, height, "height", log)
        set_capture_property(cap, cv2.CAP_PROP_FPS, fps, "fps", log)
        set_capture_property(cap, cv2.CAP_PROP_AUTO_EXPOSURE, auto_exposure, "auto exposure", log)
        set_capture_property(cap, cv2.CAP_PROP_EXPOSURE, exposure, "exposure", log)
        set_capture_property(cap, cv2.CAP_PROP_BRIGHTNESS, brightness, "brightness", log)
        set_capture_property(cap, cv2.CAP_PROP_GAIN, gain, "gain", log)
        set_capture_property(cap, cv2.CAP_PROP_CONTRAST, contrast, "contrast", log)

        if hasattr(cv2, "CAP_PROP_AUTOFOCUS"):
            set_capture_property(cap, cv2.CAP_PROP_AUTOFOCUS, autofocus, "autofocus", log)

        ok, frame = read_valid_frame(cap)
        if not ok or frame is None or frame.size == 0:
            cap.release()
            last_error = SystemExit(f"Camera {candidate_id} did not return a valid frame.")
            continue

        if is_nearly_black_frame(frame):
            if allow_black_frames:
                emit_log(
                    log,
                    f"[WARN] Camera {candidate_id} is nearly black; keeping requested camera.",
                )
            else:
                cap.release()
                last_error = SystemExit(f"Camera {candidate_id} returned a nearly black frame.")
                emit_log(log, f"[WARN] Camera {candidate_id} is nearly black; trying next camera.")
                continue

        actual_width = int(round(cap.get(cv2.CAP_PROP_FRAME_WIDTH)))
        actual_height = int(round(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)))
        actual_fps = cap.get(cv2.CAP_PROP_FPS)
        message = (
            f"[OK] Camera {candidate_id} ready at {actual_width}x{actual_height}"
            f" @ {actual_fps:.1f} fps."
        )
        if candidate_id != int(camera_id):
            message += f" Fallback from camera {camera_id}."
        emit_log(log, message)

        return cap

    if last_error is not None:
        raise last_error

    raise SystemExit(f"Camera {camera_id} could not be opened.")


class LatestFrameCamera:
    def __init__(
        self,
        capture,
        capture_factory=None,
        log=None,
        no_frame_reopen_seconds=DEFAULT_CAMERA_NO_FRAME_REOPEN_SECONDS,
        black_frame_reopen_seconds=DEFAULT_CAMERA_BLACK_FRAME_REOPEN_SECONDS,
        black_frame_mean_threshold=DEFAULT_CAMERA_BLACK_FRAME_MEAN_THRESHOLD,
        reopen_backoff_seconds=DEFAULT_CAMERA_REOPEN_BACKOFF_SECONDS,
        allow_black_frames=DEFAULT_CAMERA_ALLOW_BLACK_FRAMES,
    ):
        self._capture = capture
        self._capture_factory = capture_factory
        self._log = log
        self._no_frame_reopen_seconds = max(0.0, float(no_frame_reopen_seconds))
        self._black_frame_reopen_seconds = max(0.0, float(black_frame_reopen_seconds))
        self._black_frame_mean_threshold = float(black_frame_mean_threshold)
        self._reopen_backoff_seconds = max(0.0, float(reopen_backoff_seconds))
        self._allow_black_frames = bool(allow_black_frames)
        self._lock = threading.Lock()
        self._stop_event = threading.Event()
        self._frame = None
        self._sequence = 0
        self._last_returned_sequence = 0
        self._last_reopen_attempt_at = 0.0
        self._thread = threading.Thread(target=self._read_loop, daemon=True)
        self._thread.start()

    def _read_loop(self):
        last_valid_frame_at = time.perf_counter()
        black_frame_started_at = None

        while not self._stop_event.is_set():
            ok, frame = self._capture.read()
            now = time.perf_counter()
            if ok and frame is not None and frame.size > 0:
                last_valid_frame_at = now
                if not self._allow_black_frames and is_nearly_black_frame(frame, self._black_frame_mean_threshold):
                    if black_frame_started_at is None:
                        black_frame_started_at = now
                    elif self._black_frame_reopen_seconds > 0.0 and (
                        now - black_frame_started_at >= self._black_frame_reopen_seconds
                    ):
                        self._reopen_capture("nearly black frames")
                        last_valid_frame_at = time.perf_counter()
                        black_frame_started_at = None
                        continue
                else:
                    black_frame_started_at = None

                with self._lock:
                    self._frame = frame
                    self._sequence += 1
                continue

            if self._no_frame_reopen_seconds > 0.0 and (
                now - last_valid_frame_at >= self._no_frame_reopen_seconds
            ):
                self._reopen_capture("no valid frames")
                last_valid_frame_at = time.perf_counter()
                black_frame_started_at = None
                continue

            time.sleep(0.002)

    def _reopen_capture(self, reason):
        if self._capture_factory is None or self._stop_event.is_set():
            return

        now = time.perf_counter()
        if now - self._last_reopen_attempt_at < self._reopen_backoff_seconds:
            time.sleep(0.02)
            return

        self._last_reopen_attempt_at = now
        emit_log(self._log, f"[WARN] Camera stream stalled ({reason}); reopening camera.")

        try:
            self._capture.release()
        except cv2.error:
            pass

        try:
            self._capture = self._capture_factory()
            with self._lock:
                self._frame = None
                self._sequence += 1
                self._last_returned_sequence = self._sequence
            emit_log(self._log, "[OK] Camera stream reopened.")
        except (Exception, SystemExit) as exception:
            emit_log(self._log, f"[WARN] Camera reopen failed: {exception}")
            time.sleep(self._reopen_backoff_seconds)

    def read(self, wait=True, timeout=0.5, copy_frame=False):
        deadline = time.perf_counter() + max(0.0, timeout)

        while True:
            with self._lock:
                if self._frame is not None and self._sequence != self._last_returned_sequence:
                    frame = self._frame.copy() if copy_frame else self._frame
                    self._last_returned_sequence = self._sequence
                    return True, frame

            if not wait or self._stop_event.is_set() or time.perf_counter() >= deadline:
                return False, None

            time.sleep(0.001)

    def release(self):
        self._stop_event.set()
        if self._thread.is_alive():
            self._thread.join(timeout=0.5)
        self._capture.release()

    def isOpened(self):
        return self._capture.isOpened()

    def __getattr__(self, name):
        return getattr(self._capture, name)


def open_latest_frame_camera(camera_id, **kwargs):
    allow_black_frames = bool(kwargs.get("allow_black_frames", DEFAULT_CAMERA_ALLOW_BLACK_FRAMES))
    return LatestFrameCamera(
        open_camera(camera_id, **kwargs),
        capture_factory=lambda: open_camera(camera_id, **kwargs),
        log=kwargs.get("log"),
        allow_black_frames=allow_black_frames,
    )


def add_camera_arguments(parser, default_camera=0, preview_default=False):
    parser.add_argument("--camera", type=int, default=default_camera)
    parser.add_argument("--fallback-cameras", default="")
    parser.add_argument("--directshow-device", default=DEFAULT_DIRECTSHOW_DEVICE)
    parser.add_argument("--directshow-pixel-format", default=DEFAULT_DIRECTSHOW_PIXEL_FORMAT)
    parser.add_argument("--directshow-video-codec", default=DEFAULT_DIRECTSHOW_VIDEO_CODEC)
    parser.add_argument("--width", type=int, default=DEFAULT_CAMERA_WIDTH)
    parser.add_argument("--height", type=int, default=DEFAULT_CAMERA_HEIGHT)
    parser.add_argument("--fps", type=int, default=DEFAULT_CAMERA_FPS)
    parser.add_argument("--camera-buffer-size", type=int, default=DEFAULT_CAMERA_BUFFER_SIZE)
    parser.add_argument("--camera-auto-exposure", type=float, default=DEFAULT_CAMERA_AUTO_EXPOSURE)
    parser.add_argument("--camera-exposure", type=float, default=DEFAULT_CAMERA_EXPOSURE)
    parser.add_argument("--camera-autofocus", type=float, default=DEFAULT_CAMERA_AUTOFOCUS)
    parser.add_argument("--camera-brightness", type=float, default=DEFAULT_CAMERA_BRIGHTNESS)
    parser.add_argument("--camera-gain", type=float, default=DEFAULT_CAMERA_GAIN)
    parser.add_argument("--camera-contrast", type=float, default=DEFAULT_CAMERA_CONTRAST)
    parser.add_argument("--allow-black-frames", action="store_true")

    preview_group = parser.add_mutually_exclusive_group()
    preview_group.add_argument("--preview", dest="preview", action="store_true")
    preview_group.add_argument("--no-preview", dest="preview", action="store_false")
    parser.set_defaults(preview=preview_default)


def parse_fallback_cameras(value):
    if value is None:
        return []

    camera_ids = []
    for item in str(value).split(","):
        item = item.strip()
        if not item:
            continue
        camera_ids.append(int(item))

    return camera_ids
