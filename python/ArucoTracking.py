from pathlib import Path
import argparse
import math
import socket

import cv2

from camera_utils import add_camera_arguments, open_latest_frame_camera, parse_fallback_cameras
from preview_window_utils import (
    configure_preview_window,
    get_foreground_window,
    keep_preview_window_no_activate,
)

UDP_HOST = "127.0.0.1"
PREVIEW_WINDOW_NAME = "Aruco Tracking"
QUIT_KEY = ord("q")


def log(message):
    print(message, flush=True)


def get_aruco_module():
    aruco = getattr(cv2, "aruco", None)
    if aruco is None:
        raise SystemExit("OpenCV aruco module was not found. Install opencv-contrib-python in this environment.")
    return aruco


def get_dictionary(aruco, dictionary_name):
    normalized_name = dictionary_name.upper()
    dictionary_id = getattr(aruco, normalized_name, None)
    if dictionary_id is None:
        raise SystemExit(f"Unsupported ArUco dictionary: {dictionary_name}")

    if hasattr(aruco, "getPredefinedDictionary"):
        return aruco.getPredefinedDictionary(dictionary_id)

    if hasattr(aruco, "Dictionary_get"):
        return aruco.Dictionary_get(dictionary_id)

    raise SystemExit("This OpenCV aruco module does not expose a dictionary factory.")


def create_detector(aruco, dictionary):
    if hasattr(aruco, "DetectorParameters"):
        parameters = aruco.DetectorParameters()
    else:
        parameters = aruco.DetectorParameters_create()

    if hasattr(aruco, "ArucoDetector"):
        detector = aruco.ArucoDetector(dictionary, parameters)
        return lambda frame: detector.detectMarkers(frame)

    return lambda frame: aruco.detectMarkers(frame, dictionary, parameters=parameters)


def marker_center_and_angle(corners):
    points = corners.reshape(4, 2)
    center_x = float(points[:, 0].mean())
    center_y = float(points[:, 1].mean())
    top_edge = points[1] - points[0]
    angle_degrees = math.degrees(math.atan2(-float(top_edge[1]), float(top_edge[0])))
    return center_x, center_y, angle_degrees


def run_tracking(
    camera_id,
    fallback_camera_ids,
    dictionary_name,
    marker_id,
    udp_port,
    width,
    height,
    fps,
    camera_buffer_size,
    camera_auto_exposure,
    camera_exposure,
    camera_autofocus,
    directshow_device,
    directshow_pixel_format,
    directshow_video_codec,
    allow_black_frames,
    preview,
):
    aruco = get_aruco_module()
    dictionary = get_dictionary(aruco, dictionary_name)
    detect_markers = create_detector(aruco, dictionary)
    cap = open_latest_frame_camera(
        camera_id,
        fallback_camera_ids=fallback_camera_ids,
        width=width,
        height=height,
        fps=fps,
        buffer_size=camera_buffer_size,
        auto_exposure=camera_auto_exposure,
        exposure=camera_exposure,
        autofocus=camera_autofocus,
        directshow_device=directshow_device,
        directshow_pixel_format=directshow_pixel_format,
        directshow_video_codec=directshow_video_codec,
        allow_black_frames=allow_black_frames,
        log=log,
    )
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    udp_target = (UDP_HOST, udp_port)
    restore_focus_window = get_foreground_window() if preview else None
    preview_focus_restored = False

    log(f"[OK] Tracking {dictionary_name} ID {marker_id}.")
    log(f"[OK] Sending marker pose to Unity UDP {UDP_HOST}:{udp_port}.")
    if preview:
        cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
        configure_preview_window(cv2, PREVIEW_WINDOW_NAME, restore_focus_window)

    try:
        while True:
            success, frame = cap.read(copy_frame=preview)
            if not success or frame is None:
                continue

            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            corners, ids, _rejected = detect_markers(gray)
            if ids is not None and len(ids) > 0:
                if preview:
                    aruco.drawDetectedMarkers(frame, corners, ids)
                height, width = frame.shape[:2]

                for index, detected_id in enumerate(ids.flatten()):
                    if int(detected_id) != marker_id:
                        continue

                    center_x, center_y, angle_degrees = marker_center_and_angle(corners[index])
                    viewport_x = center_x / max(1.0, width - 1.0)
                    viewport_y = 1.0 - (center_y / max(1.0, height - 1.0))
                    payload = f"{dictionary_name},{marker_id},{viewport_x:.6f},{viewport_y:.6f},{angle_degrees:.3f}"
                    sock.sendto(payload.encode("utf-8"), udp_target)
                    if preview:
                        cv2.circle(frame, (int(center_x), int(center_y)), 8, (255, 255, 255), -1)
                    break

            if preview:
                cv2.imshow(PREVIEW_WINDOW_NAME, frame)
                if not preview_focus_restored:
                    keep_preview_window_no_activate(PREVIEW_WINDOW_NAME, restore_focus_window)
                    preview_focus_restored = True

                if cv2.waitKey(1) & 0xFF == QUIT_KEY:
                    break
    finally:
        sock.close()
        cap.release()
        if preview:
            cv2.destroyWindow(PREVIEW_WINDOW_NAME)


def main():
    parser = argparse.ArgumentParser(description="Track an ArUco marker and send its screen pose to Unity.")
    add_camera_arguments(parser, default_camera=1, preview_default=False)
    parser.add_argument("--dictionary", default="DICT_4X4_50")
    parser.add_argument("--marker-id", type=int, default=0)
    parser.add_argument("--udp-port", type=int, default=5054)
    args = parser.parse_args()

    run_tracking(
        args.camera,
        parse_fallback_cameras(args.fallback_cameras),
        args.dictionary,
        args.marker_id,
        args.udp_port,
        args.width,
        args.height,
        args.fps,
        args.camera_buffer_size,
        args.camera_auto_exposure,
        args.camera_exposure,
        args.camera_autofocus,
        args.directshow_device,
        args.directshow_pixel_format,
        args.directshow_video_codec,
        args.allow_black_frames,
        args.preview,
    )


if __name__ == "__main__":
    main()
