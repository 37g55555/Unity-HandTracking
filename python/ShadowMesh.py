"""Capture a shadow silhouette and export a 2D mesh for Unity."""

import json
import os
import sys
import argparse

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

OUTPUT_DIR = os.path.join("output", "shadowmesh")
BACKGROUND_FILE_NAME = "background.png"
CONTOUR_FILE_NAME = "shadow_contour.png"
MESH_FILE_NAME = "shadow_mesh.obj"
METADATA_FILE_NAME = "shadow_metadata.json"
CAMERA_WIDTH = 640
CAMERA_HEIGHT = 480
CAMERA_FPS = 15
EPSILON_RATIO = 0.0025
INTERIOR_SPACING = 10
ENTER_KEYS = {10, 13}
QUIT_KEY = ord("q")

cv2 = None
np = None
tr = None


def load_mesh_generation_dependencies():
    global cv2, np, tr

    if cv2 is not None and np is not None and tr is not None:
        return

    import cv2 as cv2_module
    import numpy as np_module
    import triangle as triangle_module

    cv2 = cv2_module
    np = np_module
    tr = triangle_module


def open_camera(camera_id=0):
    load_mesh_generation_dependencies()

    backend = cv2.CAP_DSHOW if hasattr(cv2, "CAP_DSHOW") else None
    cap = cv2.VideoCapture(camera_id, backend) if backend is not None else cv2.VideoCapture(camera_id)

    if not cap.isOpened():
        print(f"[ERROR] Camera {camera_id} could not be opened.")
        return None

    cap.set(cv2.CAP_PROP_FRAME_WIDTH, CAMERA_WIDTH)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAMERA_HEIGHT)
    cap.set(cv2.CAP_PROP_FPS, CAMERA_FPS)

    ok, frame = cap.read()
    if not ok or frame is None or frame.size == 0:
        cap.release()
        print(f"[ERROR] Camera {camera_id} did not return a valid frame.")
        return None

    print(f"[OK] Camera {camera_id} ready.")
    return cap


def get_background_path():
    return os.path.join(OUTPUT_DIR, BACKGROUND_FILE_NAME)


def save_background_frame(frame):
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    background_path = get_background_path()
    if not cv2.imwrite(background_path, frame):
        print(f"[ERROR] Background image could not be saved: {background_path}")
        return

    print(f"[OK] Saved {background_path}")


def load_background_frame():
    background_path = get_background_path()
    if not os.path.isfile(background_path):
        print(f"[ERROR] Background image was not found: {background_path}")
        return None

    frame = cv2.imread(background_path, cv2.IMREAD_COLOR)
    if frame is None or frame.size == 0:
        print(f"[ERROR] Background image could not be read: {background_path}")
        return None

    print(f"[OK] Loaded {background_path}")
    return frame


def capture_live(camera_id=0, use_saved_background=False):
    cap = open_camera(camera_id)
    if not cap:
        return None, None

    bg_frame = load_background_frame() if use_saved_background else None
    if use_saved_background and bg_frame is None:
        cap.release()
        cv2.destroyAllWindows()
        return None, None

    shadow_frame = None

    print("=" * 60)
    print("  Shadow Mesh Capture")
    print("=" * 60)
    if use_saved_background:
        print("  ENTER: capture shadow")
        print("  Background capture skipped; using saved background.png")
    else:
        print("  ENTER: capture background, then shadow")
    print("  q: cancel")
    print("=" * 60)

    step = 2 if use_saved_background else 1

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        display = frame.copy()

        if step == 1:
            text = "Step 1: Press ENTER to capture BACKGROUND (no object)"
        elif step == 2:
            text = "Step 2: Place object, press ENTER to capture SHADOW"
        else:
            text = "Done! Processing..."

        cv2.putText(display, text, (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)

        if bg_frame is not None and step == 2:
            bg_small = cv2.resize(bg_frame, (160, 120))
            display[10:130, display.shape[1]-170:display.shape[1]-10] = bg_small
            cv2.putText(display, "BG", (display.shape[1]-160, 25),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)

        cv2.imshow("Shadow Capture", display)
        key = cv2.waitKey(1) & 0xFF

        if key == QUIT_KEY:
            print("[INFO] Capture canceled.")
            cap.release()
            cv2.destroyAllWindows()
            return None, None

        if key in ENTER_KEYS:
            if step == 1:
                bg_frame = frame.copy()
                print("[OK] Background captured.")
                save_background_frame(bg_frame)
                step = 2
            elif step == 2:
                shadow_frame = frame.copy()
                print("[OK] Shadow captured.")
                break

    cap.release()
    cv2.destroyAllWindows()
    return bg_frame, shadow_frame


def extract_shadow_mask(bg_frame, shadow_frame):
    bg_gray = cv2.cvtColor(bg_frame, cv2.COLOR_BGR2GRAY)
    sh_gray = cv2.cvtColor(shadow_frame, cv2.COLOR_BGR2GRAY)

    diff = cv2.absdiff(bg_gray, sh_gray)
    blurred = cv2.GaussianBlur(diff, (7, 7), 0)
    _, mask = cv2.threshold(blurred, 0, 255,
                            cv2.THRESH_BINARY + cv2.THRESH_OTSU)

    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)

    return mask


def extract_contour(mask, epsilon_ratio=0.005):
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL,
                                   cv2.CHAIN_APPROX_SIMPLE)

    if len(contours) == 0:
        print("[ERROR] Shadow contour was not found.")
        return None

    main_contour = max(contours, key=cv2.contourArea)

    perimeter = cv2.arcLength(main_contour, True)
    epsilon = epsilon_ratio * perimeter
    simplified = cv2.approxPolyDP(main_contour, epsilon, True)

    contour = simplified.reshape(-1, 2)

    return contour


def generate_mesh(contour, interior_spacing=15):
    boundary = contour.astype(np.float64)
    n_boundary = len(boundary)

    x_min, y_min = boundary.min(axis=0)
    x_max, y_max = boundary.max(axis=0)

    xs = np.arange(x_min + interior_spacing/2, x_max, interior_spacing)
    ys = np.arange(y_min + interior_spacing/2, y_max, interior_spacing)
    grid_x, grid_y = np.meshgrid(xs, ys)
    grid_points = np.column_stack([grid_x.ravel(), grid_y.ravel()])

    contour_cv = boundary.reshape(-1, 1, 2).astype(np.float32)
    interior = []
    for pt in grid_points:
        dist = cv2.pointPolygonTest(contour_cv, (float(pt[0]), float(pt[1])), False)
        if dist > 0:
            interior.append(pt)

    interior = np.array(interior) if len(interior) > 0 else np.empty((0, 2))

    all_points = np.vstack([boundary, interior]) if len(interior) > 0 else boundary

    segments = np.array([(i, (i+1) % n_boundary) for i in range(n_boundary)])

    tri_input = {
        'vertices': all_points,
        'segments': segments
    }

    tri_result = tr.triangulate(tri_input, 'p')

    vertices_2d = tri_result['vertices']
    faces = tri_result['triangles']

    valid_faces = []
    for face in faces:
        centroid = np.mean(vertices_2d[face], axis=0)
        dist = cv2.pointPolygonTest(contour_cv,
                                     (float(centroid[0]), float(centroid[1])),
                                     False)
        if dist >= 0:
            valid_faces.append(face)

    valid_faces = np.array(valid_faces)

    center = np.mean(vertices_2d, axis=0)
    vertices_normalized = vertices_2d - center
    scale = np.max(vertices_normalized.max(axis=0) - vertices_normalized.min(axis=0))
    if scale > 0:
        vertices_normalized /= scale

    vertices_normalized[:, 1] *= -1.0
    if len(valid_faces) > 0:
        valid_faces = valid_faces[:, [0, 2, 1]]

    vertices_3d = np.column_stack([
        vertices_normalized,
        np.zeros(len(vertices_normalized))
    ])

    return vertices_3d, valid_faces, n_boundary, center, scale


def save_contour_image(filepath, mask, contour):
    preview = np.zeros((mask.shape[0], mask.shape[1], 3), dtype=np.uint8)
    cv2.drawContours(preview, [contour.astype(np.int32)], -1, (255, 255, 255), 2)
    cv2.imwrite(filepath, preview)


def save_obj(filepath, vertices, faces):
    temp_filepath = f"{filepath}.tmp"
    try:
        with open(temp_filepath, 'w', encoding='utf-8', newline='\n') as f:
            for v in vertices:
                f.write(f"v {v[0]:.6f} {v[1]:.6f} {v[2]:.6f}\n")

            f.write("\n")

            for face in faces:
                f.write(f"f {face[0]+1} {face[1]+1} {face[2]+1}\n")

        os.replace(temp_filepath, filepath)
    finally:
        if os.path.exists(temp_filepath):
            os.remove(temp_filepath)


def save_metadata(filepath, n_vertices, n_faces, n_boundary, center, scale,
                  epsilon_ratio, interior_spacing):
    metadata = {
        "n_vertices": n_vertices,
        "n_triangles": n_faces,
        "n_boundary": n_boundary,
        "boundary_indices": list(range(n_boundary)),
        "center_offset": center.tolist(),
        "scale_factor": float(scale),
        "epsilon_ratio": epsilon_ratio,
        "interior_spacing": interior_spacing
    }

    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(metadata, f, indent=2, ensure_ascii=False)


def process_shadow(bg_frame, shadow_frame):
    load_mesh_generation_dependencies()
    if not os.path.isdir(OUTPUT_DIR):
        print(f"[ERROR] Output directory was not found: {OUTPUT_DIR}")
        return False

    print("[INFO] Generating shadow mesh...")
    mask = extract_shadow_mask(bg_frame, shadow_frame)

    contour = extract_contour(mask, EPSILON_RATIO)
    if contour is None:
        return False

    vertices_3d, faces, n_boundary, center, scale = generate_mesh(
        contour, INTERIOR_SPACING
    )

    contour_path = os.path.join(OUTPUT_DIR, CONTOUR_FILE_NAME)
    save_contour_image(contour_path, mask, contour)

    obj_path = os.path.join(OUTPUT_DIR, MESH_FILE_NAME)
    save_obj(obj_path, vertices_3d, faces)

    meta_path = os.path.join(OUTPUT_DIR, METADATA_FILE_NAME)
    save_metadata(meta_path, len(vertices_3d), len(faces), n_boundary,
                  center, scale, EPSILON_RATIO, INTERIOR_SPACING)

    print(f"[OK] Saved {contour_path}")
    print(f"[OK] Saved {obj_path}")
    print(f"[OK] Saved {meta_path}")

    return True


def validate_existing_mesh():
    obj_path = os.path.join(OUTPUT_DIR, MESH_FILE_NAME)
    metadata_path = os.path.join(OUTPUT_DIR, METADATA_FILE_NAME)

    if not os.path.isfile(obj_path):
        print(f"[ERROR] Existing mesh was not found: {obj_path}")
        return False

    if not os.path.isfile(metadata_path):
        print(f"[ERROR] Existing metadata was not found: {metadata_path}")
        return False

    if os.path.getsize(obj_path) <= 0 or os.path.getsize(metadata_path) <= 0:
        print("[ERROR] Existing shadow mesh files are empty.")
        return False

    try:
        with open(metadata_path, "r", encoding="utf-8") as f:
            json.load(f)
    except (OSError, json.JSONDecodeError):
        print(f"[ERROR] Existing metadata could not be read: {metadata_path}")
        return False

    print(f"[OK] Using existing {obj_path}")
    print(f"[OK] Using existing {metadata_path}")
    return True


def main():
    parser = argparse.ArgumentParser(description="Capture a shadow mesh for Unity.")
    parser.add_argument("--mode", choices=["live", "file"], default="live")
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--bg", dest="use_background", action="store_true")

    args = parser.parse_args()

    if args.mode == "live":
        bg_frame, shadow_frame = capture_live(args.camera, args.use_background)
        if bg_frame is None or shadow_frame is None:
            sys.exit(1)
        success = process_shadow(bg_frame, shadow_frame)

    elif args.mode == "file":
        success = validate_existing_mesh()

    if not success:
        sys.exit(1)


if __name__ == "__main__":
    main()
