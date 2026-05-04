# 환골 — 그림자 캡처 및 Mesh 생성

`shadow_capture.py`는 배경 프레임과 그림자 프레임을 비교해서 그림자 영역을 추출하고, Unity가 런타임 로드할 수 있는 2D OBJ mesh를 생성합니다.

## 설치

```bash
pip install -r requirements.txt
```

## Windows 전시 실행

전시에서는 repository root의 `run_exhibition.sh`로 실행하는 것을 권장합니다.

```bash
cd ..
chmod +x run_exhibition.sh
./run_exhibition.sh
```

`run_exhibition.sh`는 아래 작업을 순서대로 처리합니다.

1. `CAP_II/.venv` 생성 및 requirements 설치
2. 그림자 캡처 창 실행
3. `output/shadow_mesh.obj` 생성
4. Windows Unity 프로젝트의 `sf3d_io/live_shadow/`로 OBJ/metadata 복사
5. MediaPipe hand tracking 실행

## IP Camera Live Capture

iOS IP Camera 앱의 MJPEG stream 주소를 사용합니다.

```bash
python shadow_capture.py --mode live --camera-url http://192.168.0.12:8081/video
```

path 없이 입력해도 자동으로 `/video`가 붙습니다.

```bash
python shadow_capture.py --mode live --camera-url 192.168.0.12:8081
```

캡처 순서:

1. 카메라 창이 뜨면 오브제 없이 빈 배경 상태에서 스페이스바를 누릅니다.
2. 오브제를 놓아 그림자를 만든 뒤 다시 스페이스바를 누릅니다.
3. 처리 후 `output/` 폴더에 결과가 저장됩니다.
4. ESC를 누르면 취소합니다.

## USB Webcam Live Capture

IP camera 대신 USB webcam을 직접 사용할 수도 있습니다.

```bash
python shadow_capture.py --mode live --camera 0 --no-camera-fallback
```

- `--camera 0`: 사용할 카메라 번호
- `--no-camera-fallback`: 지정한 카메라가 실패해도 다른 카메라를 자동으로 잡지 않음

카메라 번호 확인:

```bash
python ../tools/list_cameras.py
```

## Test Mode

웹캠 없이 합성 그림자로 파이프라인을 테스트합니다.

```bash
python shadow_capture.py --mode test
```

## File Mode

이미 저장된 배경 이미지와 그림자 이미지를 입력으로 사용할 수 있습니다.

```bash
python shadow_capture.py --mode file --bg background.png --input shadow.png
```

## Mesh Density Parameters

```bash
python shadow_capture.py --mode live --spacing 5 --boundary-spacing 5 --epsilon 0.0015
```

- `--spacing`: 내부 vertex 간격입니다. 작을수록 내부 vertex가 많아집니다.
- `--boundary-spacing`: 윤곽선 vertex 간격입니다. 작을수록 외곽선 vertex가 많아집니다.
- `--epsilon`: 윤곽선 단순화 비율입니다. 작을수록 원본 윤곽선을 더 많이 유지합니다.
- `--threshold`: Otsu 자동 threshold가 잘 안 먹을 때 직접 이진화 값을 지정합니다.

기본값은 `--epsilon 0.002`, `--spacing 8`, `--boundary-spacing 8`입니다.

## Unity Coordinate Notes

Unity는 Y축이 위로 증가하고 OpenCV 이미지는 Y축이 아래로 증가합니다. 그래서 기본 출력 OBJ는 Unity용으로 Y축을 반전합니다.

Y축 반전 시 mesh normal이 뒤집히지 않도록 face winding도 함께 반전합니다.

원본 이미지 좌표 방향 그대로 OBJ를 만들고 싶으면 아래 옵션을 사용합니다.

```bash
python shadow_capture.py --mode live --no-unity-flip-y
```

## Output Files

결과 파일은 `output/`에 저장됩니다.

| 파일 | 설명 |
|------|------|
| `output/shadow_mesh.obj` | Unity 로드용 2D mesh |
| `output/shadow_metadata.json` | boundary index, vertex 수, triangle 수 |
| `output/shadow_mask.png` | 이진 마스크 |
| `output/shadow_contour.png` | 윤곽선 및 vertex 확인 이미지 |
| `output/shadow_mesh_preview.png` | 삼각분할 preview |

Unity 전시 실행에서는 이 중 `shadow_mesh.obj`와 `shadow_metadata.json`이 `sf3d_io/live_shadow/`로 복사되어 `LiveMeshLoader`가 감지합니다.
