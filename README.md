# Unity AI Shadow Pipeline

중앙대학교 예술공학부 캡스톤 디자인 프로젝트 2 전시용 입력 파이프라인입니다.

사용자의 그림자를 캡처해 2D OBJ mesh로 만들고, Unity에서 해당 mesh를 표시한 뒤 MediaPipe 손 입력으로 윤곽선 vertex를 잡아 변형합니다.

## Repository Layout

- `UnityProject/`
  - 전시용 Unity 프로젝트 소스
  - 실행 씬: `Assets/Scenes/ShadowPrototype.unity`
- `CAP_II/`
  - 그림자 캡처 및 OBJ 생성 파이프라인
  - 실행 파일: `shadow_capture.py`
- `3d Hand Tracking/`
  - MediaPipe Hand Landmarker 기반 손 추적
  - 실행 파일: `main.py`
- `run_exhibition.sh`
  - Windows 전시용 WSL 런처
  - Python 환경 설치, Unity 스크립트 동기화, 그림자 캡처, OBJ 복사, 핸드트래킹 실행을 순서대로 처리

## Current Windows Exhibition Flow

최종 전시 기준 실행 구조는 `WSL backend + Windows Unity frontend + IP camera stream`입니다.

1. iOS IP Camera 앱을 켜고 MJPEG 주소를 확인합니다.
2. WSL 터미널에서 `./run_exhibition.sh`를 실행합니다.
3. 캡처 창에서 스페이스바로 배경을 먼저 캡처합니다.
4. 오브젝트를 놓고 다시 스페이스바를 눌러 그림자를 캡처합니다.
5. WSL이 `CAP_II/output/shadow_mesh.obj`를 생성합니다.
6. WSL이 생성된 OBJ/metadata를 Windows Unity 프로젝트의 `sf3d_io/live_shadow/`로 복사합니다.
7. WSL이 MediaPipe hand tracking을 실행하고 Windows Unity UDP `5052` 포트로 손 좌표를 보냅니다.
8. Windows Unity에서 `ShadowPrototype` 씬을 열고 Play를 누르면 mesh가 로드되고 손 입력으로 변형됩니다.

Unity는 Windows에서 Python backend를 직접 실행하지 않고, WSL에서 이미 실행 중인 backend output과 UDP packet을 기다립니다.

## Quick Start On Windows + WSL

WSL 안에서 repository 폴더로 이동한 뒤 실행합니다.

```bash
chmod +x run_exhibition.sh
./run_exhibition.sh
```

IP camera 주소가 다르면 실행할 때 바꿉니다.

```bash
IP_CAMERA_URL="http://192.168.0.12:8081/video" ./run_exhibition.sh
```

Unity 프로젝트 위치가 자동 감지되지 않으면 직접 지정합니다.

```bash
UNITY_PROJECT_PATH="/mnt/c/Users/<WindowsUser>/Desktop/My project" ./run_exhibition.sh
```

WSL에서 Windows Unity로 UDP가 안 들어오면 Windows 방화벽에서 UDP `5052` 인바운드를 허용해야 합니다.

## USB Webcam Option

iOS IP Camera 대신 Windows/WSL에서 USB 웹캠 2대를 사용할 수도 있습니다.

```bash
USE_IP_CAMERA=0 CAPTURE_CAMERA=0 HAND_CAMERA=1 ./run_exhibition.sh
```

- `CAPTURE_CAMERA`: 그림자 캡처용 카메라 번호
- `HAND_CAMERA`: MediaPipe 손 추적용 카메라 번호
- 카메라가 반대로 잡히면 두 값을 서로 바꾸면 됩니다.

카메라 번호 확인용 스크립트:

```bash
python tools/list_cameras.py
```

## Hand Interaction Model

현재 조작은 전시장에서 바로 이해할 수 있게 단순화되어 있습니다.

1. 검지를 오브젝트 윤곽선 가까이 가져갑니다.
2. Unity가 가장 가까운 boundary vertex를 자동 선택합니다.
3. 선택된 vertex가 하이라이트되고 `PINCH TO GRAB` 안내가 보입니다.
4. 엄지와 검지를 pinch 하면 선택된 vertex를 grab 합니다.
5. pinch 상태에서 손을 움직이면 주변 vertex가 함께 부드럽게 당겨집니다.
6. 손을 펴면 release 됩니다.

오른쪽 세로 슬라이더는 감도 조절이 아니라 `grab 영향 범위` 조절입니다. 슬라이더 근처에서 엄지와 중지를 pinch 하면 손으로도 조절할 수 있습니다.

## Important Interaction Decisions

- 오브젝트 전체 이동은 사용하지 않습니다.
- rotation / scale 제어는 사용하지 않습니다.
- `tear` 기능은 제거되었습니다.
- 현재는 `hover -> pinch -> grab -> drag` 흐름만 사용합니다.
- 손 입력 감도는 중간 정도 고정값으로 유지합니다.

## Unity Visual Feedback

Unity Game view 안에서 아래 정보를 확인할 수 있습니다.

- 손 검지 위치 마커
- 엄지 위치 마커
- 현재 선택된 boundary vertex 하이라이트
- 손가락과 선택 vertex 사이 연결선
- `PINCH TO GRAB` / `GRAB` 상태 텍스트
- 현재 deformation 영향 범위 표시 링
- 주변 vertex 영향 범위 조절용 오른쪽 세로 슬라이더

MediaPipe 카메라 창을 보지 않아도 Unity 화면만 보고 어느 점이 선택됐는지 확인할 수 있습니다.

## Shadow Mesh Capture Notes

- `CAP_II/shadow_capture.py`는 기본적으로 `--epsilon 0.002`, `--spacing 8`, `--boundary-spacing 8`을 사용합니다.
- OpenCV 이미지 좌표계와 Unity 좌표계가 반대라서 OBJ 출력 시 기본적으로 Y축을 반전합니다.
- Y축 반전 후 mesh normal이 뒤집히지 않도록 face winding도 함께 반전합니다.
- 원본 이미지 방향 그대로 내보내고 싶으면 `--no-unity-flip-y`를 붙이면 됩니다.
- `--camera-url`에 `192.168.0.12:8081`처럼 path 없이 넣으면 자동으로 `http://192.168.0.12:8081/video`로 보정합니다.

## Output Files

그림자 캡처 결과는 `CAP_II/output/`에 저장됩니다.

- `shadow_mesh.obj`: Unity 로드용 2D mesh
- `shadow_metadata.json`: boundary index, vertex/triangle 수
- `shadow_mask.png`: 그림자 이진 마스크
- `shadow_contour.png`: 윤곽선 확인 이미지
- `shadow_mesh_preview.png`: mesh 삼각분할 preview
- `deformed_shadow.png`: Unity에서 변형 후 Enter/S 키로 저장한 최종 PNG

전시 실행 시 `run_exhibition.sh`가 `shadow_mesh.obj`와 `shadow_metadata.json`을 Windows Unity 프로젝트의 `sf3d_io/live_shadow/`로 복사합니다.

## Key Unity Runtime Scripts

- `UnityProject/Assets/Scripts/Runtime/ExhibitionFlowController.cs`
- `UnityProject/Assets/Scripts/Runtime/LiveMeshLoader.cs`
- `UnityProject/Assets/Scripts/Runtime/HandLandmarkUdpReceiver.cs`
- `UnityProject/Assets/Scripts/Runtime/MediaPipeMeshDeformationInput.cs`
- `UnityProject/Assets/Scripts/Runtime/MediaPipeInteractionVisualizer.cs`
- `UnityProject/Assets/Scripts/Runtime/DeformationControlPanel.cs`
- `UnityProject/Assets/Scripts/Runtime/ShadowDeformer.cs`

## Debug Checklist

- Unity Console에 `Shadow mesh loaded`가 뜨면 OBJ 로드는 성공입니다.
- Unity Console에 `HandLandmarkUdpReceiver received first packet`이 뜨면 WSL에서 Unity로 UDP가 들어온 것입니다.
- mesh는 보이는데 손 반응이 없으면 UDP `5052` 방화벽, `UNITY_UDP_HOST`, 그리고 `MediaPipeMeshDeformationInput`의 projection debug 옵션을 확인합니다.
- WSL에서 `logs/hand_tracking.log`를 보면 MediaPipe 실행 로그를 확인할 수 있습니다.

## Requirements

Python requirements:

- `CAP_II/requirements.txt`
- `3d Hand Tracking/requirements.txt`

Unity requirements:

- Unity 6 `6000.4.0f1`
- URP 프로젝트
- Input System package

Unity 캐시 폴더(`Library`, `Temp`, `Logs`, `UserSettings`)는 git에 포함하지 않습니다.
