from pathlib import Path
import argparse
from dataclasses import dataclass
import math
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
from camera_utils import add_camera_arguments, open_latest_frame_camera, parse_fallback_cameras

PACKET_WIDTH = 1920
PACKET_HEIGHT = 1080
UDP_HOST = "127.0.0.1"
UDP_PORT = 5053
MODEL_PATH = Path(__file__).resolve().parent / "MediaPipe.task"
PREVIEW_WINDOW_NAME = "Hand Tracking"
QUIT_KEY = ord("q")


@dataclass
class HandCandidate:
    index: int
    landmarks: object
    points: list
    center: tuple
    bbox: tuple
    area_ratio: float
    span_ratio: float
    average_z: float


def log(message):
    print(message, flush=True)


def ensure_model_exists():
    if not MODEL_PATH.exists():
        raise SystemExit(f"MediaPipe model was not found: {MODEL_PATH}")


def create_landmarker(max_hands):
    base_options = python.BaseOptions(model_asset_path=str(MODEL_PATH))
    options = vision.HandLandmarkerOptions(
        base_options=base_options,
        running_mode=vision.RunningMode.VIDEO,
        num_hands=max(1, int(max_hands)),
        min_hand_detection_confidence=0.5,
        min_hand_presence_confidence=0.5,
        min_tracking_confidence=0.5,
    )
    return vision.HandLandmarker.create_from_options(options)


def build_udp_payload(hand_landmarks_list):
    data = []
    for hand_landmarks in hand_landmarks_list:
        for landmark in hand_landmarks:
            x = landmark.x * PACKET_WIDTH
            y = (1.0 - landmark.y) * PACKET_HEIGHT
            z = landmark.z * PACKET_WIDTH
            data.extend([round(x, 3), round(y, 3), round(z, 5)])
    return data


def clamp(value, minimum, maximum):
    return max(minimum, min(maximum, value))


def point_distance(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


def bbox_area(bbox):
    left, top, right, bottom = bbox
    return max(0.0, right - left) * max(0.0, bottom - top)


def bbox_center(bbox):
    left, top, right, bottom = bbox
    return ((left + right) * 0.5, (top + bottom) * 0.5)


def bbox_iou(a, b):
    left = max(a[0], b[0])
    top = max(a[1], b[1])
    right = min(a[2], b[2])
    bottom = min(a[3], b[3])
    intersection = bbox_area((left, top, right, bottom))
    if intersection <= 0.0:
        return 0.0

    union = bbox_area(a) + bbox_area(b) - intersection
    return intersection / union if union > 0.0 else 0.0


def expand_bbox(bbox, expand_x, expand_y, width, height):
    left, top, right, bottom = bbox
    box_width = right - left
    box_height = bottom - top
    return (
        clamp(left - box_width * expand_x, 0.0, width - 1.0),
        clamp(top - box_height * expand_y, 0.0, height - 1.0),
        clamp(right + box_width * expand_x, 0.0, width - 1.0),
        clamp(bottom + box_height * expand_y, 0.0, height - 1.0),
    )


def point_inside_bbox(point, bbox):
    x, y = point
    return bbox[0] <= x <= bbox[2] and bbox[1] <= y <= bbox[3]


def build_hand_candidate(index, hand_landmarks, frame_width, frame_height):
    points = []
    z_values = []
    for landmark in hand_landmarks:
        x = clamp(float(landmark.x), 0.0, 1.0) * (frame_width - 1.0)
        y = clamp(float(landmark.y), 0.0, 1.0) * (frame_height - 1.0)
        points.append((x, y))
        z_values.append(float(landmark.z))

    if not points:
        return None

    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    bbox = (min(xs), min(ys), max(xs), max(ys))
    hand_area = bbox_area(bbox)
    frame_area = max(1.0, frame_width * frame_height)
    span = max(bbox[2] - bbox[0], bbox[3] - bbox[1])
    palm_indices = [0, 5, 9, 13, 17]
    palm_points = [points[i] for i in palm_indices if i < len(points)]
    center = (
        sum(point[0] for point in palm_points) / len(palm_points),
        sum(point[1] for point in palm_points) / len(palm_points),
    )

    return HandCandidate(
        index=index,
        landmarks=hand_landmarks,
        points=points,
        center=center,
        bbox=bbox,
        area_ratio=hand_area / frame_area,
        span_ratio=span / max(1.0, max(frame_width, frame_height)),
        average_z=sum(z_values) / len(z_values),
    )


def suppress_overlapping_bboxes(bboxes, iou_threshold=0.45):
    kept = []
    for bbox in sorted(bboxes, key=lambda item: (item[4], bbox_area(item[:4])), reverse=True):
        if all(bbox_iou(bbox[:4], kept_bbox[:4]) < iou_threshold for kept_bbox in kept):
            kept.append(bbox)
    return kept


class PersonHandFilter:
    def __init__(
        self,
        enabled,
        fallback_hands,
        max_output_hands,
        bbox_expand_x,
        bbox_expand_y,
        detection_interval_frames,
        detection_width,
        lock_seconds,
        lost_grace_seconds,
        min_hand_area_ratio,
        min_hand_span_ratio,
        hand_lock_distance_ratio,
        z_score_weight,
        person_selection_mode,
        person_center_x,
        person_center_y,
        person_center_fallback_width,
        person_center_fallback_height,
    ):
        self.enabled = enabled
        self.fallback_hands = fallback_hands
        self.max_output_hands = max(1, int(max_output_hands))
        self.bbox_expand_x = max(0.0, bbox_expand_x)
        self.bbox_expand_y = max(0.0, bbox_expand_y)
        self.detection_interval_frames = max(1, detection_interval_frames)
        self.detection_width = max(160, detection_width)
        self.lock_seconds = max(0.0, lock_seconds)
        self.lost_grace_seconds = max(0.0, lost_grace_seconds)
        self.min_hand_area_ratio = max(0.0, min_hand_area_ratio)
        self.min_hand_span_ratio = max(0.0, min_hand_span_ratio)
        self.hand_lock_distance_ratio = max(0.01, hand_lock_distance_ratio)
        self.z_score_weight = max(0.0, z_score_weight)
        self.person_selection_mode = (
            person_selection_mode
            if person_selection_mode in ("largest", "center", "hand")
            else "largest"
        )
        self.person_center_x = clamp(float(person_center_x), 0.0, 1.0)
        self.person_center_y = clamp(float(person_center_y), 0.0, 1.0)
        self.person_center_fallback_width = clamp(float(person_center_fallback_width), 0.05, 1.0)
        self.person_center_fallback_height = clamp(float(person_center_fallback_height), 0.05, 1.0)
        self.frame_index = 0
        self.person_bbox = None
        self.expanded_person_bbox = None
        self.center_fallback_bbox = None
        self.person_locked_at = 0.0
        self.last_person_seen = 0.0
        self.active_hand_centers = []
        self.active_hand_last_seen = 0.0
        self.last_selected_indices = set()

        self.hog = None
        if self.enabled:
            self.hog = cv2.HOGDescriptor()
            self.hog.setSVMDetector(cv2.HOGDescriptor_getDefaultPeopleDetector())

    def select_hands(self, frame, result):
        hand_landmarks_list = result.hand_landmarks or []
        self.last_selected_indices = set()
        if not hand_landmarks_list:
            self.active_hand_centers = []
            self.center_fallback_bbox = None
            return []

        height, width = frame.shape[:2]
        candidates = []
        for index, hand_landmarks in enumerate(hand_landmarks_list):
            candidate = build_hand_candidate(index, hand_landmarks, width, height)
            if candidate is not None:
                candidates.append(candidate)

        if not self.enabled:
            self.last_selected_indices = set(range(len(hand_landmarks_list)))
            return hand_landmarks_list

        now = time.perf_counter()
        self._update_person_lock(frame, now, candidates)
        valid_candidates = self._filter_candidates(candidates, width, height)
        if not valid_candidates:
            if now - self.active_hand_last_seen > self.lost_grace_seconds:
                self.active_hand_centers = []
            return []

        selected = self._choose_candidates(valid_candidates, width, height)
        self.active_hand_centers = [candidate.center for candidate in selected]
        self.active_hand_last_seen = now
        self.last_selected_indices = {candidate.index for candidate in selected}
        return [candidate.landmarks for candidate in selected]

    def _update_person_lock(self, frame, now, hand_candidates):
        self.frame_index += 1
        height, width = frame.shape[:2]
        if self.frame_index % self.detection_interval_frames != 1:
            if self.person_bbox and now - self.last_person_seen <= self.lost_grace_seconds:
                self.expanded_person_bbox = expand_bbox(
                    self.person_bbox,
                    self.bbox_expand_x,
                    self.bbox_expand_y,
                    width,
                    height,
                )
            return

        detected_bboxes = self._detect_people(frame)
        if detected_bboxes:
            self.person_bbox = self._select_person_bbox(detected_bboxes, width, height, now, hand_candidates)
            self.last_person_seen = now
            self.expanded_person_bbox = expand_bbox(
                self.person_bbox,
                self.bbox_expand_x,
                self.bbox_expand_y,
                width,
                height,
            )
            return

        if self.person_bbox and now - self.last_person_seen <= self.lost_grace_seconds:
            self.expanded_person_bbox = expand_bbox(
                self.person_bbox,
                self.bbox_expand_x,
                self.bbox_expand_y,
                width,
                height,
            )
            return

        self.person_bbox = None
        self.expanded_person_bbox = None

    def _detect_people(self, frame):
        if self.hog is None:
            return []

        height, width = frame.shape[:2]
        scale = 1.0
        detection_frame = frame
        if width > self.detection_width:
            scale = self.detection_width / float(width)
            detection_frame = cv2.resize(frame, (self.detection_width, int(height * scale)))

        rects, weights = self.hog.detectMultiScale(
            detection_frame,
            winStride=(8, 8),
            padding=(8, 8),
            scale=1.05,
        )

        bboxes = []
        for index, rect in enumerate(rects):
            x, y, box_width, box_height = rect
            score = float(weights[index]) if index < len(weights) else 1.0
            if score < 0.0:
                continue

            inv_scale = 1.0 / scale
            left = clamp(x * inv_scale, 0.0, width - 1.0)
            top = clamp(y * inv_scale, 0.0, height - 1.0)
            right = clamp((x + box_width) * inv_scale, 0.0, width - 1.0)
            bottom = clamp((y + box_height) * inv_scale, 0.0, height - 1.0)
            bboxes.append((left, top, right, bottom, score))

        return suppress_overlapping_bboxes(bboxes)

    def _select_person_bbox(self, detected_bboxes, width, height, now, hand_candidates):
        if self.person_bbox is None:
            self.person_locked_at = now
            return max(
                detected_bboxes,
                key=lambda bbox: self._score_person_bbox(bbox, width, height, None, hand_candidates),
            )[:4]

        current_center = bbox_center(self.person_bbox)

        def score(bbox):
            return self._score_person_bbox(bbox, width, height, current_center, hand_candidates)

        best = max(detected_bboxes, key=score)[:4]
        lock_active = now - self.person_locked_at < self.lock_seconds
        if (
            self.person_selection_mode == "largest"
            and lock_active
            and bbox_iou(self.person_bbox, best) <= 0.05
        ):
            return self.person_bbox

        if self.person_selection_mode == "largest" and bbox_iou(self.person_bbox, best) <= 0.0:
            previous_area = bbox_area(self.person_bbox)
            best_area = bbox_area(best)
            if previous_area > best_area * 0.5:
                return self.person_bbox

        if bbox_iou(self.person_bbox, best) <= 0.5:
            self.person_locked_at = now

        return best

    def _score_person_bbox(self, bbox, width, height, previous_center, hand_candidates):
        candidate_bbox = bbox[:4]
        candidate_center = bbox_center(candidate_bbox)
        diagonal = max(1.0, math.hypot(width, height))
        size_score = bbox_area(candidate_bbox) / max(1.0, width * height)
        detection_score = bbox[4] * 0.05

        if self.person_selection_mode == "center":
            target = self._target_frame_center(width, height)
            target_score = 1.0 - min(1.0, point_distance(candidate_center, target) / diagonal)
            continuity_score = 0.0
            if self.person_bbox is not None:
                continuity_score += bbox_iou(self.person_bbox, candidate_bbox) * 1.4
            if previous_center is not None:
                continuity_score += (
                    1.0 - min(1.0, point_distance(candidate_center, previous_center) / diagonal)
                ) * 0.35
            return target_score * 4.0 + continuity_score + size_score * 0.7 + detection_score

        if self.person_selection_mode == "hand":
            target = self._target_hand_center(hand_candidates, width, height)
            target_score = 1.0 - min(1.0, point_distance(candidate_center, target) / diagonal)
            continuity_score = bbox_iou(self.person_bbox, candidate_bbox) * 1.2 if self.person_bbox is not None else 0.0
            return target_score * 4.0 + continuity_score + size_score * 0.7 + detection_score

        continuity_score = 0.0
        if self.person_bbox is not None:
            continuity_score += bbox_iou(self.person_bbox, candidate_bbox) * 3.0
        if previous_center is not None:
            continuity_score += 1.0 - min(1.0, point_distance(candidate_center, previous_center) / diagonal)
        return continuity_score + size_score + detection_score

    def _target_frame_center(self, width, height):
        return (
            self.person_center_x * (width - 1.0),
            self.person_center_y * (height - 1.0),
        )

    def _target_hand_center(self, hand_candidates, width, height):
        if self.active_hand_centers:
            return (
                sum(center[0] for center in self.active_hand_centers) / len(self.active_hand_centers),
                sum(center[1] for center in self.active_hand_centers) / len(self.active_hand_centers),
            )

        if hand_candidates:
            frame_center = self._target_frame_center(width, height)
            return min(hand_candidates, key=lambda candidate: point_distance(candidate.center, frame_center)).center

        return self._target_frame_center(width, height)

    def _build_center_fallback_bbox(self, width, height):
        center_x, center_y = self._target_frame_center(width, height)
        half_width = width * self.person_center_fallback_width * 0.5
        half_height = height * self.person_center_fallback_height * 0.5
        return (
            clamp(center_x - half_width, 0.0, width - 1.0),
            clamp(center_y - half_height, 0.0, height - 1.0),
            clamp(center_x + half_width, 0.0, width - 1.0),
            clamp(center_y + half_height, 0.0, height - 1.0),
        )

    def _filter_candidates(self, candidates, width, height):
        self.center_fallback_bbox = None
        candidates = [
            candidate
            for candidate in candidates
            if candidate.area_ratio >= self.min_hand_area_ratio
            and candidate.span_ratio >= self.min_hand_span_ratio
        ]

        if self.expanded_person_bbox is not None:
            return [
                candidate
                for candidate in candidates
                if point_inside_bbox(candidate.center, self.expanded_person_bbox)
            ]

        if self.fallback_hands and self.person_selection_mode == "center":
            self.center_fallback_bbox = self._build_center_fallback_bbox(width, height)
            return [
                candidate
                for candidate in candidates
                if point_inside_bbox(candidate.center, self.center_fallback_bbox)
            ]

        return candidates if self.fallback_hands else []

    def _choose_candidates(self, candidates, width, height):
        lock_distance = self.hand_lock_distance_ratio * max(width, height)
        person_center = bbox_center(self.expanded_person_bbox) if self.expanded_person_bbox else None
        frame_diagonal = math.hypot(width, height)

        def score(candidate):
            value = candidate.area_ratio * 12.0 + candidate.span_ratio * 2.0
            if person_center is not None:
                value += 1.0 - min(1.0, point_distance(candidate.center, person_center) / frame_diagonal)
            if self.active_hand_centers:
                distance = min(point_distance(candidate.center, center) for center in self.active_hand_centers)
                value += max(0.0, 1.0 - distance / lock_distance) * 4.0
            value -= abs(candidate.average_z) * self.z_score_weight
            return value

        return sorted(candidates, key=score, reverse=True)[: self.max_output_hands]

    def draw_debug(self, display):
        if not self.enabled:
            return

        if self.person_bbox is not None:
            draw_bbox(display, self.person_bbox, (255, 180, 0), 2)
        if self.expanded_person_bbox is not None:
            draw_bbox(display, self.expanded_person_bbox, (0, 200, 255), 2)
        if self.center_fallback_bbox is not None:
            draw_bbox(display, self.center_fallback_bbox, (200, 80, 255), 2)


def draw_bbox(display, bbox, color, thickness):
    left, top, right, bottom = (int(round(value)) for value in bbox)
    cv2.rectangle(display, (left, top), (right, bottom), color, thickness)


def draw_hand_landmarks(display, result, selected_indices=None):
    if not result.hand_landmarks:
        return

    height, width = display.shape[:2]
    for index, hand_landmarks in enumerate(result.hand_landmarks):
        is_selected = selected_indices is None or index in selected_indices
        color = (0, 255, 0) if is_selected else (70, 70, 220)
        radius = 4 if is_selected else 3
        points = []
        for landmark in hand_landmarks:
            x = int(max(0.0, min(1.0, landmark.x)) * (width - 1))
            y = int(max(0.0, min(1.0, landmark.y)) * (height - 1))
            points.append((x, y))

        for point in points:
            cv2.circle(display, point, radius, color, -1)


def adjust_frame_brightness(frame, gain, offset):
    gain = max(0.0, float(gain))
    offset = float(offset)
    if gain == 1.0 and offset == 0.0:
        return frame

    return cv2.convertScaleAbs(frame, alpha=gain, beta=offset)


def run_tracking(
    camera_id,
    fallback_camera_ids,
    width,
    height,
    fps,
    camera_buffer_size,
    camera_auto_exposure,
    camera_exposure,
    camera_autofocus,
    camera_brightness,
    camera_gain,
    camera_contrast,
    directshow_device,
    directshow_pixel_format,
    directshow_video_codec,
    frame_gain,
    frame_brightness_offset,
    max_hands,
    max_output_hands,
    person_filter,
    person_filter_fallback_hands,
    person_bbox_expand_x,
    person_bbox_expand_y,
    person_detection_interval_frames,
    person_detection_width,
    person_lock_seconds,
    person_lost_grace_seconds,
    min_hand_area_ratio,
    min_hand_span_ratio,
    hand_lock_distance_ratio,
    hand_z_score_weight,
    person_selection_mode,
    person_center_x,
    person_center_y,
    person_center_fallback_width,
    person_center_fallback_height,
    allow_black_frames,
    preview,
):
    ensure_model_exists()

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
        brightness=camera_brightness,
        gain=camera_gain,
        contrast=camera_contrast,
        directshow_device=directshow_device,
        directshow_pixel_format=directshow_pixel_format,
        directshow_video_codec=directshow_video_codec,
        allow_black_frames=allow_black_frames,
        log=log,
    )
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    landmarker = create_landmarker(max_hands)
    hand_filter = PersonHandFilter(
        enabled=person_filter,
        fallback_hands=person_filter_fallback_hands,
        max_output_hands=max_output_hands,
        bbox_expand_x=person_bbox_expand_x,
        bbox_expand_y=person_bbox_expand_y,
        detection_interval_frames=person_detection_interval_frames,
        detection_width=person_detection_width,
        lock_seconds=person_lock_seconds,
        lost_grace_seconds=person_lost_grace_seconds,
        min_hand_area_ratio=min_hand_area_ratio,
        min_hand_span_ratio=min_hand_span_ratio,
        hand_lock_distance_ratio=hand_lock_distance_ratio,
        z_score_weight=hand_z_score_weight,
        person_selection_mode=person_selection_mode,
        person_center_x=person_center_x,
        person_center_y=person_center_y,
        person_center_fallback_width=person_center_fallback_width,
        person_center_fallback_height=person_center_fallback_height,
    )
    udp_target = (UDP_HOST, UDP_PORT)
    last_timestamp_ms = 0
    restore_focus_window = get_foreground_window() if preview else None
    preview_focus_restored = False

    log(f"[OK] Sending landmarks to Unity UDP {UDP_HOST}:{UDP_PORT}.")
    if preview:
        cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
        configure_preview_window(cv2, PREVIEW_WINDOW_NAME, restore_focus_window)

    try:
        while True:
            success, frame = cap.read(copy_frame=preview)
            if not success or frame is None:
                continue

            frame = adjust_frame_brightness(frame, frame_gain, frame_brightness_offset)
            frame = cv2.flip(frame, 1)
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)

            timestamp_ms = int(time.perf_counter() * 1000)
            if timestamp_ms <= last_timestamp_ms:
                timestamp_ms = last_timestamp_ms + 1
            last_timestamp_ms = timestamp_ms

            result = landmarker.detect_for_video(mp_image, timestamp_ms)
            selected_hands = hand_filter.select_hands(frame, result)
            hand_count = len(selected_hands)
            if hand_count > 0:
                payload = build_udp_payload(selected_hands)
                sock.sendto(str(payload).encode("utf-8"), udp_target)

            if preview:
                hand_filter.draw_debug(frame)
                draw_hand_landmarks(frame, result, hand_filter.last_selected_indices)
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
        if preview:
            cv2.destroyWindow(PREVIEW_WINDOW_NAME)


def main():
    parser = argparse.ArgumentParser(description="Send MediaPipe hand landmarks to Unity.")
    add_camera_arguments(parser, default_camera=1, preview_default=False)
    parser.add_argument("--frame-gain", type=float, default=1.0)
    parser.add_argument("--frame-brightness-offset", type=float, default=0.0)
    parser.add_argument("--max-hands", type=int, default=4)
    parser.add_argument("--max-output-hands", type=int, default=2)
    parser.add_argument("--person-filter", action="store_true")
    parser.add_argument("--person-filter-fallback-hands", action="store_true")
    parser.add_argument("--person-bbox-expand-x", type=float, default=0.35)
    parser.add_argument("--person-bbox-expand-y", type=float, default=0.25)
    parser.add_argument("--person-detection-interval-frames", type=int, default=8)
    parser.add_argument("--person-detection-width", type=int, default=480)
    parser.add_argument("--person-lock-seconds", type=float, default=1.0)
    parser.add_argument("--person-lost-grace-seconds", type=float, default=0.8)
    parser.add_argument("--min-hand-area-ratio", type=float, default=0.001)
    parser.add_argument("--min-hand-span-ratio", type=float, default=0.035)
    parser.add_argument("--hand-lock-distance-ratio", type=float, default=0.18)
    parser.add_argument("--hand-z-score-weight", type=float, default=0.04)
    parser.add_argument(
        "--person-selection-mode",
        choices=("largest", "center", "hand"),
        default="largest",
    )
    parser.add_argument("--person-center-x", type=float, default=0.5)
    parser.add_argument("--person-center-y", type=float, default=0.5)
    parser.add_argument("--person-center-fallback-width", type=float, default=0.7)
    parser.add_argument("--person-center-fallback-height", type=float, default=1.0)
    args = parser.parse_args()

    run_tracking(
        args.camera,
        parse_fallback_cameras(args.fallback_cameras),
        args.width,
        args.height,
        args.fps,
        args.camera_buffer_size,
        args.camera_auto_exposure,
        args.camera_exposure,
        args.camera_autofocus,
        args.camera_brightness,
        args.camera_gain,
        args.camera_contrast,
        args.directshow_device,
        args.directshow_pixel_format,
        args.directshow_video_codec,
        args.frame_gain,
        args.frame_brightness_offset,
        args.max_hands,
        args.max_output_hands,
        args.person_filter,
        args.person_filter_fallback_hands,
        args.person_bbox_expand_x,
        args.person_bbox_expand_y,
        args.person_detection_interval_frames,
        args.person_detection_width,
        args.person_lock_seconds,
        args.person_lost_grace_seconds,
        args.min_hand_area_ratio,
        args.min_hand_span_ratio,
        args.hand_lock_distance_ratio,
        args.hand_z_score_weight,
        args.person_selection_mode,
        args.person_center_x,
        args.person_center_y,
        args.person_center_fallback_width,
        args.person_center_fallback_height,
        args.allow_black_frames,
        args.preview,
    )


if __name__ == "__main__":
    main()
