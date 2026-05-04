import argparse
import sys
import time

import cv2


def build_backend_candidates():
    backends = []
    if sys.platform.startswith("win"):
        if hasattr(cv2, "CAP_DSHOW"):
            backends.append(("DirectShow", cv2.CAP_DSHOW))
        if hasattr(cv2, "CAP_MSMF"):
            backends.append(("MSMF", cv2.CAP_MSMF))
    elif sys.platform == "darwin" and hasattr(cv2, "CAP_AVFOUNDATION"):
        backends.append(("AVFoundation", cv2.CAP_AVFOUNDATION))

    backends.append(("Default", None))
    return backends


def try_open_camera(index, backend):
    if backend is None:
        cap = cv2.VideoCapture(index)
    else:
        cap = cv2.VideoCapture(index, backend)

    if cap is None or not cap.isOpened():
        if cap is not None:
            cap.release()
        return None, None

    frame = None
    for _ in range(12):
        ok, candidate = cap.read()
        if ok and candidate is not None and candidate.size > 0:
            frame = candidate
            break
        time.sleep(0.05)

    if frame is None:
        cap.release()
        return None, None

    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    fps = cap.get(cv2.CAP_PROP_FPS)
    return cap, (width, height, fps)


def main():
    parser = argparse.ArgumentParser(description="List OpenCV camera indexes.")
    parser.add_argument("--max-index", type=int, default=8, help="Scan indexes from 0 to this value.")
    parser.add_argument("--preview", action="store_true", help="Show a short preview for each found camera.")
    args = parser.parse_args()

    print("Scanning cameras...")
    print(f"Platform: {sys.platform}")
    print()

    found = []
    for index in range(args.max_index + 1):
        for backend_name, backend in build_backend_candidates():
            cap, info = try_open_camera(index, backend)
            if cap is None:
                continue

            width, height, fps = info
            print(f"[OK] camera {index}: backend={backend_name}, size={width}x{height}, fps={fps:.1f}")
            found.append((index, backend_name))

            if args.preview:
                preview_title = f"camera {index} / {backend_name} - press any key"
                for _ in range(30):
                    ok, frame = cap.read()
                    if not ok:
                        break
                    cv2.imshow(preview_title, frame)
                    if cv2.waitKey(30) >= 0:
                        break
                cv2.destroyWindow(preview_title)

            cap.release()
            break

    print()
    if not found:
        print("No cameras found. Check camera permissions and close apps using the camera.")
        return

    print("Use these indexes in Unity/Python:")
    print("  shadow_capture.py --mode live --camera <index> --no-camera-fallback")
    print("  main.py --camera <index> --no-camera-fallback")


if __name__ == "__main__":
    main()
