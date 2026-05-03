# 환골 — 그림자 캡처 및 Mesh 생성

## 설치

```bash
pip install opencv-python numpy scipy triangle trimesh matplotlib
```

## 사용법

### 1. 테스트 모드 (웹캠 없이)

합성 그림자로 파이프라인 전체를 테스트:

```bash
python shadow_capture.py --mode test
```

### 2. 웹캠 라이브 캡처 (맥북)

```bash
python shadow_capture.py --mode live
```

1. 카메라 창이 뜨면, **오브제 없이 빈 배경 상태**에서 **스페이스바**
2. **오브제를 놓아 그림자를 만들고** 다시 **스페이스바**
3. 자동으로 처리 후 `output/` 폴더에 결과 저장

**맥북 팁:**
- 조명: 스탠드 조명이나 스마트폰 플래시를 한쪽에서 비추면 선명한 그림자가 생김
- 배경: 흰 종이나 밝은 책상 위에서 촬영
- 카메라 권한: 시스템 설정 > 개인정보 보호 > 카메라 에서 터미널/VSCode 허용

### 3. 파라미터 조절

```bash
# 전체 vertex를 더 많이 생성 (기본값보다 촘촘함)
python shadow_capture.py --mode test --spacing 5 --boundary-spacing 5 --epsilon 0.0015

# 내부 vertex만 더 촘촘하게 (변형 부드러움 증가, 성능 감소)
python shadow_capture.py --mode test --spacing 5

# 경계 vertex만 더 촘촘하게 (외곽선 형태 보존 증가)
python shadow_capture.py --mode test --boundary-spacing 5

# 수동 threshold (Otsu가 잘 안 먹힐 때)
python shadow_capture.py --mode live --threshold 30
```

기본값은 `--epsilon 0.002`, `--spacing 8`, `--boundary-spacing 8`입니다.

### 4. 결과 시각화

```bash
python view_mesh.py
```

## 출력 파일

| 파일 | 설명 |
|------|------|
| `output/shadow_mesh.obj` | Unity 로드용 2D mesh (z=0 평면) |
| `output/shadow_metadata.json` | boundary 인덱스, vertex 수, 스케일 등 |
| `output/shadow_mask.png` | 이진 마스크 |
| `output/shadow_contour.png` | 윤곽선 + vertex 시각화 |
| `output/shadow_mesh_preview.png` | 삼각분할 시각화 |

## Unity에서 사용

1. `shadow_mesh.obj`를 Unity 프로젝트의 Assets 폴더에 복사
2. Import Settings → **Read/Write Enabled = True**
3. Scene에 배치 → Material: Unlit/Color (Black)
4. `ShadowDeformer.cs` 컴포넌트 추가

또는 런타임 로드 (`LiveMeshLoader` + `ObjParser`)를 사용하면
Assets에 복사할 필요 없이 자동으로 로드됨.
