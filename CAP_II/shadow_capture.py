"""
환골 — 그림자 캡처 및 2D Mesh 생성 도구
========================================

사용법:
  1. 라이브 캡처 (웹캠):
     python shadow_capture.py --mode live

  2. 테스트 모드 (웹캠 없이, 합성 그림자로 테스트):
     python shadow_capture.py --mode test

  3. 이미지 파일에서:
     python shadow_capture.py --mode file --input shadow_photo.jpg

출력:
  output/shadow_mesh.obj          — Unity 로드용 2D mesh
  output/shadow_metadata.json     — boundary 인덱스, vertex 수 등
  output/shadow_mask.png          — 이진 마스크 (확인용)
  output/shadow_contour.png       — 윤곽선 시각화 (확인용)
  output/shadow_mesh_preview.png  — mesh 삼각분할 시각화 (확인용)
"""

import cv2
import numpy as np
import json
import os
import sys
import argparse
import time
from urllib.parse import urlparse
import triangle as tr
import trimesh

OUTPUT_DIR = "output"
DEFAULT_IP_CAMERA_URL = os.environ.get("IP_CAMERA_URL", "")


# ============================================================
# Phase A: 그림자 캡처 — 배경 차분으로 그림자 영역 추출
# ============================================================

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


def build_camera_id_candidates(camera_id=0, allow_fallback=True):
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


def open_ip_camera(camera_url, width=1280, height=720):
    resolved_url = normalize_camera_url(camera_url)
    print(f"[INFO] IP camera stream: {resolved_url}")

    cap = cv2.VideoCapture(resolved_url)
    if cap is None or not cap.isOpened():
        if cap is not None:
            cap.release()
        print(f"[ERROR] IP 카메라 스트림을 열지 못했습니다: {resolved_url}")
        return None, resolved_url, "IPCamera"

    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)

    for _ in range(30):
        ok, frame = cap.read()
        if ok and frame is not None and frame.size > 0:
            print(f"[OK] IP 카메라 연결 성공: {resolved_url}")
            return cap, resolved_url, "IPCamera"
        time.sleep(0.1)

    cap.release()
    print(f"[ERROR] IP 카메라에서 프레임을 읽지 못했습니다: {resolved_url}")
    return None, resolved_url, "IPCamera"


def open_camera(camera_id=0, width=1280, height=720, allow_fallback=True, camera_url=""):
    """
    Windows 전시 세팅에서는 DirectShow/MSMF를 우선 사용하고,
    macOS 개발 세팅에서는 AVFoundation을 우선 사용한다.
    """
    if camera_url:
        return open_ip_camera(camera_url, width, height)

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

            cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)

            success = False
            for _ in range(12):
                ok, frame = cap.read()
                if ok and frame is not None and frame.size > 0:
                    success = True
                    break
                time.sleep(0.05)

            if success:
                print(f"[OK] 카메라 연결 성공: camera_id={candidate_id}, backend={backend_name}")
                return cap, candidate_id, backend_name

            cap.release()

    print("[ERROR] 사용 가능한 카메라를 열지 못했습니다.")
    print(f"        시도한 조합: {', '.join(tried)}")
    print("        확인할 것:")
    print("        1) 다른 Python/Unity/카메라 앱이 카메라를 잡고 있지 않은지")
    print("        2) Windows 설정 > 개인정보 및 보안 > 카메라 권한")
    print("        3) 웹캠 2대 사용 시 --camera 번호가 맞는지")
    print("        4) IP 카메라 사용 시 같은 네트워크이고 URL이 /video 스트림인지")
    return None, None, None


def capture_live(camera_id=0, allow_camera_fallback=True, camera_url=""):
    """
    웹캠 라이브 캡처.
    1) 스페이스바: 배경 캡처 (오브제 없는 상태)
    2) 오브제를 놓고 스페이스바: 그림자 캡처
    3) ESC: 종료
    """
    cap, resolved_camera_id, backend_name = open_camera(
        camera_id,
        allow_fallback=allow_camera_fallback,
        camera_url=camera_url)
    if cap is None:
        return None, None

    bg_frame = None
    shadow_frame = None

    print("=" * 60)
    print("  환골 — 그림자 캡처 도구 (라이브 모드)")
    print("=" * 60)
    print()
    print("  [Step 1] 오브제 없이 빈 배경만 보이게 한 뒤")
    print("           스페이스바를 눌러 배경을 캡처하세요.")
    print()
    print("  [Step 2] 오브제를 놓아 그림자를 만든 뒤")
    print("           스페이스바를 눌러 그림자를 캡처하세요.")
    print()
    print(f"  Camera: {resolved_camera_id}, backend={backend_name}")
    print()
    print("  ESC: 종료")
    print("=" * 60)

    step = 1

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        display = frame.copy()

        # 상태 안내 텍스트
        if step == 1:
            text = "Step 1: Press SPACE to capture BACKGROUND (no object)"
        elif step == 2:
            text = "Step 2: Place object, press SPACE to capture SHADOW"
        else:
            text = "Done! Processing..."

        cv2.putText(display, text, (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)

        # 배경 캡처 완료 시 배경 미리보기 표시
        if bg_frame is not None and step == 2:
            bg_small = cv2.resize(bg_frame, (160, 120))
            display[10:130, display.shape[1]-170:display.shape[1]-10] = bg_small
            cv2.putText(display, "BG", (display.shape[1]-160, 25),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)

        cv2.imshow("Shadow Capture", display)
        key = cv2.waitKey(1) & 0xFF

        if key == 27:  # ESC
            print("\n취소됨.")
            cap.release()
            cv2.destroyAllWindows()
            return None, None

        if key == 32:  # Space
            if step == 1:
                bg_frame = frame.copy()
                print("[OK] 배경 캡처 완료. 이제 오브제를 놓고 스페이스바를 누르세요.")
                step = 2
            elif step == 2:
                shadow_frame = frame.copy()
                print("[OK] 그림자 캡처 완료. 처리 중...")
                break

    cap.release()
    cv2.destroyAllWindows()
    return bg_frame, shadow_frame


def create_test_shadow(width=640, height=480):
    """
    웹캠 없이 테스트용 합성 그림자 생성.
    밝은 배경 위에 불규칙한 형태의 어두운 그림자.
    """
    # 배경: 밝은 회색
    bg = np.ones((height, width, 3), dtype=np.uint8) * 220

    # 그림자 프레임: 배경 + 어두운 그림자 영역
    shadow = bg.copy()

    # 불규칙한 그림자 형태 생성 (여러 타원 합성)
    mask = np.zeros((height, width), dtype=np.uint8)

    # 메인 형태
    cv2.ellipse(mask, (width//2, height//2), (120, 80), 30, 0, 360, 255, -1)
    # 돌출부 1
    cv2.ellipse(mask, (width//2 + 80, height//2 - 40), (50, 30), -20, 0, 360, 255, -1)
    # 돌출부 2
    cv2.ellipse(mask, (width//2 - 60, height//2 + 50), (40, 60), 10, 0, 360, 255, -1)

    # 스무딩
    mask = cv2.GaussianBlur(mask, (15, 15), 0)
    _, mask = cv2.threshold(mask, 128, 255, cv2.THRESH_BINARY)

    # 그림자 적용 (어두운 영역)
    shadow[mask > 0] = [60, 60, 60]

    print("[TEST] 합성 그림자 생성 완료 (640x480)")
    return bg, shadow


def extract_shadow_mask(bg_frame, shadow_frame, threshold_value=None):
    """
    배경 차분으로 그림자 영역 추출.

    Input: 배경 프레임, 그림자 프레임
    Output: 이진 마스크 (그림자=255, 배경=0)
    """
    bg_gray = cv2.cvtColor(bg_frame, cv2.COLOR_BGR2GRAY)
    sh_gray = cv2.cvtColor(shadow_frame, cv2.COLOR_BGR2GRAY)

    # 배경 차분
    diff = cv2.absdiff(bg_gray, sh_gray)

    # 노이즈 제거
    blurred = cv2.GaussianBlur(diff, (7, 7), 0)

    # 이진화 (Otsu 자동 threshold)
    if threshold_value is None:
        _, mask = cv2.threshold(blurred, 0, 255,
                                cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    else:
        _, mask = cv2.threshold(blurred, threshold_value, 255,
                                cv2.THRESH_BINARY)

    # 모폴로지 연산: 구멍 메우기 + 노이즈 제거
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)

    return mask


def extract_contour(mask, epsilon_ratio=0.005):
    """
    이진 마스크에서 주 윤곽선 추출 + 단순화.

    Input: 이진 마스크
    Output: 단순화된 윤곽선 (N×2 numpy 배열)
    """
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL,
                                   cv2.CHAIN_APPROX_SIMPLE)

    if len(contours) == 0:
        print("[ERROR] 그림자를 찾을 수 없습니다. 조명/배경을 확인하세요.")
        return None

    # 가장 큰 contour 선택
    main_contour = max(contours, key=cv2.contourArea)

    # 면적 체크
    area = cv2.contourArea(main_contour)
    total = mask.shape[0] * mask.shape[1]
    ratio = area / total
    print(f"  그림자 면적: {area:.0f}px ({ratio*100:.1f}% of frame)")

    if ratio < 0.01:
        print("[WARN] 그림자가 너무 작습니다 (1% 미만). 조명/오브제를 확인하세요.")
    if ratio > 0.5:
        print("[WARN] 그림자가 너무 큽니다 (50% 초과). 배경 캡처를 확인하세요.")

    # 윤곽선 단순화
    perimeter = cv2.arcLength(main_contour, True)
    epsilon = epsilon_ratio * perimeter
    simplified = cv2.approxPolyDP(main_contour, epsilon, True)

    contour = simplified.reshape(-1, 2)
    print(f"  윤곽선: {len(main_contour)} → {len(contour)} points (epsilon={epsilon:.1f})")

    return contour


# ============================================================
# Phase B: 2D Mesh 생성 — Constrained Delaunay Triangulation
# ============================================================

def resample_closed_contour(contour, spacing):
    """
    닫힌 윤곽선을 일정 간격으로 재샘플링해 boundary vertex를 늘림.
    spacing이 작을수록 외곽선 vertex가 촘촘해진다.
    """
    if spacing is None or spacing <= 0 or len(contour) < 2:
        return contour.astype(np.float64)

    pts = contour.astype(np.float64)
    resampled = []

    for start, end in zip(pts, np.roll(pts, -1, axis=0)):
        segment = end - start
        length = np.linalg.norm(segment)
        if length <= 1e-6:
            continue

        n_steps = max(1, int(np.ceil(length / spacing)))
        for i in range(n_steps):
            t = i / n_steps
            resampled.append(start + segment * t)

    if len(resampled) == 0:
        return pts

    return np.array(resampled, dtype=np.float64)


def generate_mesh(contour, interior_spacing=8, boundary_spacing=8, flip_y_for_unity=True):
    """
    윤곽선 → 내부 점 생성 → Constrained Delaunay → OBJ 내보내기.

    Input: 윤곽선 (N×2), 내부 점 간격 (px), 경계 점 간격 (px)
    Output: vertices_3d, faces, n_boundary
    """
    boundary_base = contour.astype(np.float64)
    boundary = resample_closed_contour(boundary_base, boundary_spacing)
    n_boundary = len(boundary)
    print(f"  경계 vertex: {len(boundary_base)} → {n_boundary}개 (spacing={boundary_spacing}px)")

    # ── 내부 점 생성 (균일 그리드 + 내부 판정) ──
    x_min, y_min = boundary.min(axis=0)
    x_max, y_max = boundary.max(axis=0)

    # 그리드 생성
    xs = np.arange(x_min + interior_spacing/2, x_max, interior_spacing)
    ys = np.arange(y_min + interior_spacing/2, y_max, interior_spacing)
    grid_x, grid_y = np.meshgrid(xs, ys)
    grid_points = np.column_stack([grid_x.ravel(), grid_y.ravel()])

    # 내부 판정 (cv2.pointPolygonTest)
    contour_cv = boundary.reshape(-1, 1, 2).astype(np.float32)
    interior = []
    for pt in grid_points:
        dist = cv2.pointPolygonTest(contour_cv, (float(pt[0]), float(pt[1])), False)
        if dist > 0:  # 내부에 있는 점만 (경계 제외)
            interior.append(pt)

    interior = np.array(interior) if len(interior) > 0 else np.empty((0, 2))
    print(f"  내부 점: {len(interior)}개 (spacing={interior_spacing}px)")

    # ── 전체 vertex 구성 ──
    all_points = np.vstack([boundary, interior]) if len(interior) > 0 else boundary

    # ── Constrained Delaunay Triangulation ──
    segments = np.array([(i, (i+1) % n_boundary) for i in range(n_boundary)])

    tri_input = {
        'vertices': all_points,
        'segments': segments
    }

    tri_result = tr.triangulate(tri_input, 'p')  # 'p' = constrained

    vertices_2d = tri_result['vertices']
    faces = tri_result['triangles']

    print(f"  삼각분할: {len(vertices_2d)} vertices, {len(faces)} triangles")

    # ── 외부 삼각형 제거 ──
    valid_faces = []
    for face in faces:
        centroid = np.mean(vertices_2d[face], axis=0)
        dist = cv2.pointPolygonTest(contour_cv,
                                     (float(centroid[0]), float(centroid[1])),
                                     False)
        if dist >= 0:  # 내부 또는 경계
            valid_faces.append(face)

    valid_faces = np.array(valid_faces)
    print(f"  유효 삼각형: {len(valid_faces)} (외부 {len(faces) - len(valid_faces)}개 제거)")

    # ── 좌표 정규화 (Unity 단위) ──
    center = np.mean(vertices_2d, axis=0)
    vertices_normalized = vertices_2d - center
    scale = np.max(vertices_normalized.max(axis=0) - vertices_normalized.min(axis=0))
    if scale > 0:
        vertices_normalized /= scale

    if flip_y_for_unity:
        # OpenCV image coordinates grow downward, while Unity's 2D plane uses upward Y.
        vertices_normalized[:, 1] *= -1.0
        valid_faces = valid_faces[:, [0, 2, 1]]

    # z=0 평면
    vertices_3d = np.column_stack([
        vertices_normalized,
        np.zeros(len(vertices_normalized))
    ])

    return vertices_3d, valid_faces, n_boundary, center, scale


def save_obj(filepath, vertices, faces):
    """OBJ 파일 저장."""
    with open(filepath, 'w') as f:
        f.write("# 환골 — Shadow Mesh\n")
        f.write(f"# Vertices: {len(vertices)}, Faces: {len(faces)}\n\n")

        for v in vertices:
            f.write(f"v {v[0]:.6f} {v[1]:.6f} {v[2]:.6f}\n")

        f.write("\n")

        for face in faces:
            # OBJ는 1-indexed
            f.write(f"f {face[0]+1} {face[1]+1} {face[2]+1}\n")


def save_metadata(filepath, n_vertices, n_faces, n_boundary, center, scale,
                  epsilon_ratio, interior_spacing, boundary_spacing,
                  flip_y_for_unity):
    """메타데이터 JSON 저장."""
    metadata = {
        "n_vertices": n_vertices,
        "n_triangles": n_faces,
        "n_boundary": n_boundary,
        "boundary_indices": list(range(n_boundary)),
        "center_offset": center.tolist(),
        "scale_factor": float(scale),
        "epsilon_ratio": epsilon_ratio,
        "interior_spacing": interior_spacing,
        "boundary_spacing": boundary_spacing,
        "flip_y_for_unity": bool(flip_y_for_unity)
    }

    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(metadata, f, indent=2, ensure_ascii=False)


# ============================================================
# 시각화
# ============================================================

def visualize_mask(mask, contour, output_path):
    """마스크 + 윤곽선 시각화."""
    vis = cv2.cvtColor(mask, cv2.COLOR_GRAY2BGR)

    if contour is not None:
        contour_cv = contour.reshape(-1, 1, 2).astype(np.int32)
        cv2.drawContours(vis, [contour_cv], -1, (0, 255, 0), 2)

        # vertex 점 표시
        for pt in contour:
            cv2.circle(vis, (int(pt[0]), int(pt[1])), 3, (0, 0, 255), -1)

    cv2.imwrite(output_path, vis)


def visualize_mesh(vertices_2d, faces, boundary_n, image_size, output_path):
    """mesh 삼각분할 시각화."""
    vis = np.ones((image_size[1], image_size[0], 3), dtype=np.uint8) * 240

    for face in faces:
        pts = vertices_2d[face].astype(np.int32)
        # 삼각형 외곽선
        cv2.line(vis, tuple(pts[0]), tuple(pts[1]), (200, 200, 200), 1)
        cv2.line(vis, tuple(pts[1]), tuple(pts[2]), (200, 200, 200), 1)
        cv2.line(vis, tuple(pts[2]), tuple(pts[0]), (200, 200, 200), 1)

    # boundary vertices (빨간색)
    for i in range(boundary_n):
        pt = vertices_2d[i].astype(int)
        cv2.circle(vis, (pt[0], pt[1]), 3, (0, 0, 255), -1)

    # interior vertices (파란색)
    for i in range(boundary_n, len(vertices_2d)):
        pt = vertices_2d[i].astype(int)
        cv2.circle(vis, (pt[0], pt[1]), 2, (255, 0, 0), -1)

    cv2.imwrite(output_path, vis)


# ============================================================
# 메인
# ============================================================

def process_shadow(bg_frame, shadow_frame, epsilon_ratio=0.002,
                   interior_spacing=8, boundary_spacing=8,
                   flip_y_for_unity=True,
                   threshold_value=None):
    """
    전체 파이프라인: 배경/그림자 프레임 → OBJ + 메타데이터.
    """
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    print("\n[1/4] 그림자 마스크 추출...")
    mask = extract_shadow_mask(bg_frame, shadow_frame, threshold_value)
    cv2.imwrite(os.path.join(OUTPUT_DIR, "shadow_mask.png"), mask)

    print("\n[2/4] 윤곽선 추출...")
    contour = extract_contour(mask, epsilon_ratio)
    if contour is None:
        return False

    # 윤곽선 시각화
    visualize_mask(mask, contour, os.path.join(OUTPUT_DIR, "shadow_contour.png"))

    print("\n[3/4] 2D Mesh 생성...")
    vertices_3d, faces, n_boundary, center, scale = generate_mesh(
        contour, interior_spacing, boundary_spacing, flip_y_for_unity
    )

    print("\n[4/4] 파일 저장...")

    # OBJ 저장
    obj_path = os.path.join(OUTPUT_DIR, "shadow_mesh.obj")
    save_obj(obj_path, vertices_3d, faces)
    print(f"  → {obj_path}")

    # 메타데이터 저장
    meta_path = os.path.join(OUTPUT_DIR, "shadow_metadata.json")
    save_metadata(meta_path, len(vertices_3d), len(faces), n_boundary,
                  center, scale, epsilon_ratio, interior_spacing,
                  boundary_spacing, flip_y_for_unity)
    print(f"  → {meta_path}")

    # mesh 시각화 (원본 좌표계)
    # vertices_3d를 다시 원본 픽셀 좌표로 역변환
    vertices_pixel = vertices_3d[:, :2].copy()
    if flip_y_for_unity:
        vertices_pixel[:, 1] *= -1.0
    vertices_pixel = vertices_pixel * scale + center
    visualize_mesh(vertices_pixel, faces, n_boundary,
                   (shadow_frame.shape[1], shadow_frame.shape[0]),
                   os.path.join(OUTPUT_DIR, "shadow_mesh_preview.png"))
    print(f"  → {os.path.join(OUTPUT_DIR, 'shadow_mesh_preview.png')}")

    # ── 결과 요약 ──
    print("\n" + "=" * 60)
    print("  완료!")
    print(f"  Vertices: {len(vertices_3d)} ({n_boundary} boundary + {len(vertices_3d)-n_boundary} interior)")
    print(f"  Triangles: {len(faces)}")
    print(f"  OBJ: {obj_path}")
    print("=" * 60)

    return True


def main():
    parser = argparse.ArgumentParser(description="환골 — 그림자 캡처 및 2D Mesh 생성")
    parser.add_argument("--mode", choices=["live", "test", "file"],
                        default="test",
                        help="캡처 모드: live(웹캠), test(합성), file(파일)")
    parser.add_argument("--input", type=str, default=None,
                        help="file 모드: 입력 이미지 경로")
    parser.add_argument("--bg", type=str, default=None,
                        help="file 모드: 배경 이미지 경로")
    parser.add_argument("--camera", type=int, default=0,
                        help="live 모드: 카메라 ID (기본: 0)")
    parser.add_argument("--camera-url", type=str, default=DEFAULT_IP_CAMERA_URL,
                        help="MJPEG/IP 카메라 URL. 예: http://192.168.0.12:8081/video")
    parser.add_argument("--no-camera-fallback", action="store_true",
                        help="지정한 카메라 ID만 사용합니다. 웹캠 2대 전시 세팅에서 권장.")
    parser.add_argument("--epsilon", type=float, default=0.002,
                        help="윤곽선 단순화 비율 (기본: 0.002)")
    parser.add_argument("--spacing", type=float, default=8,
                        help="내부 vertex 간격 px (기본: 8, 작을수록 vertex 증가)")
    parser.add_argument("--boundary-spacing", type=float, default=8,
                        help="경계 vertex 간격 px (기본: 8, 작을수록 boundary vertex 증가)")
    parser.add_argument("--no-unity-flip-y", action="store_true",
                        help="Unity 좌표계 보정용 Y축 반전을 끕니다.")
    parser.add_argument("--threshold", type=int, default=None,
                        help="이진화 threshold (기본: Otsu 자동)")

    args = parser.parse_args()

    if args.spacing <= 0:
        parser.error("--spacing은 0보다 커야 합니다.")
    if args.boundary_spacing <= 0:
        parser.error("--boundary-spacing은 0보다 커야 합니다.")

    if args.mode == "live":
        bg_frame, shadow_frame = capture_live(
            args.camera,
            allow_camera_fallback=not args.no_camera_fallback,
            camera_url=args.camera_url)
        if bg_frame is None or shadow_frame is None:
            sys.exit(1)

    elif args.mode == "test":
        bg_frame, shadow_frame = create_test_shadow()

    elif args.mode == "file":
        if args.input is None or args.bg is None:
            print("[ERROR] file 모드에서는 --input과 --bg를 모두 지정해야 합니다.")
            print("  예: python shadow_capture.py --mode file --bg background.jpg --input shadow.jpg")
            sys.exit(1)
        bg_frame = cv2.imread(args.bg)
        shadow_frame = cv2.imread(args.input)
        if bg_frame is None or shadow_frame is None:
            print("[ERROR] 이미지를 불러올 수 없습니다.")
            sys.exit(1)

    success = process_shadow(
        bg_frame, shadow_frame,
        epsilon_ratio=args.epsilon,
        interior_spacing=args.spacing,
        boundary_spacing=args.boundary_spacing,
        flip_y_for_unity=not args.no_unity_flip_y,
        threshold_value=args.threshold
    )

    if not success:
        sys.exit(1)


if __name__ == "__main__":
    main()
