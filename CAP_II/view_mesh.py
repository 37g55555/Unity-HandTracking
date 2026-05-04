"""
환골 — Mesh 뷰어
================

생성된 OBJ 파일을 3D로 시각화 (Unity 없이 확인용).
matplotlib로 간단히 보여줌.

사용법:
  python view_mesh.py
  python view_mesh.py --obj output/shadow_mesh.obj
"""

import numpy as np
import json
import argparse
import os

def load_obj(path):
    """OBJ 파일 로드."""
    vertices = []
    faces = []
    with open(path, 'r') as f:
        for line in f:
            if line.startswith('v '):
                parts = line.split()
                vertices.append([float(parts[1]), float(parts[2]), float(parts[3])])
            elif line.startswith('f '):
                parts = line.split()
                face = [int(p.split('/')[0]) - 1 for p in parts[1:]]
                faces.append(face)
    return np.array(vertices), np.array(faces)

def main():
    parser = argparse.ArgumentParser(description="환골 — Mesh 뷰어")
    parser.add_argument("--obj", default="output/shadow_mesh.obj", help="OBJ 파일 경로")
    parser.add_argument("--meta", default="output/shadow_metadata.json", help="메타데이터 경로")
    args = parser.parse_args()

    if not os.path.exists(args.obj):
        print(f"[ERROR] {args.obj} 파일을 찾을 수 없습니다.")
        print("        먼저 shadow_capture.py를 실행하세요.")
        return

    vertices, faces = load_obj(args.obj)

    # 메타데이터
    n_boundary = 0
    if os.path.exists(args.meta):
        with open(args.meta, 'r') as f:
            meta = json.load(f)
        n_boundary = meta.get('n_boundary', 0)
        print(f"메타데이터: {meta['n_vertices']} vertices, {meta['n_triangles']} triangles, {n_boundary} boundary")

    print(f"OBJ 로드: {len(vertices)} vertices, {len(faces)} faces")
    print(f"좌표 범위: x=[{vertices[:,0].min():.3f}, {vertices[:,0].max():.3f}], "
          f"y=[{vertices[:,1].min():.3f}, {vertices[:,1].max():.3f}]")

    try:
        import matplotlib
        matplotlib.use('Agg')  # 화면 없는 환경
        import matplotlib.pyplot as plt
        from matplotlib.collections import PolyCollection
        import matplotlib.tri as mtri

        fig, axes = plt.subplots(1, 2, figsize=(14, 6))

        # ── 왼쪽: 삼각분할 wireframe ──
        ax = axes[0]
        ax.set_title(f"Mesh ({len(vertices)} verts, {len(faces)} tris)", fontsize=12)
        ax.set_aspect('equal')

        triang = mtri.Triangulation(vertices[:, 0], vertices[:, 1], faces)
        ax.triplot(triang, 'b-', linewidth=0.3, alpha=0.5)

        # boundary vertices
        if n_boundary > 0:
            ax.plot(vertices[:n_boundary, 0], vertices[:n_boundary, 1],
                    'r.', markersize=3, label=f'Boundary ({n_boundary})')
            ax.plot(vertices[n_boundary:, 0], vertices[n_boundary:, 1],
                    'b.', markersize=1, alpha=0.5, label=f'Interior ({len(vertices)-n_boundary})')
            ax.legend(fontsize=8)

        ax.invert_yaxis()

        # ── 오른쪽: 채워진 mesh (Unity에서 보이는 모습) ──
        ax2 = axes[1]
        ax2.set_title("Filled (Unity preview)", fontsize=12)
        ax2.set_aspect('equal')

        polygons = [vertices[face, :2] for face in faces]
        collection = PolyCollection(polygons, facecolors='#1a1a1a',
                                     edgecolors='#333333', linewidths=0.2)
        ax2.add_collection(collection)
        ax2.autoscale_view()
        ax2.set_facecolor('white')
        ax2.invert_yaxis()

        plt.tight_layout()
        out_path = "output/mesh_3d_view.png"
        plt.savefig(out_path, dpi=150, bbox_inches='tight')
        print(f"\n시각화 저장: {out_path}")
        plt.close()

    except ImportError:
        print("\n[INFO] matplotlib가 없어 시각화를 건너뜁니다.")
        print("       pip install matplotlib 로 설치하면 시각화가 가능합니다.")


if __name__ == "__main__":
    main()
