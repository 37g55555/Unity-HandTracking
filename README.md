# Unity AI Shadow Pipeline

중앙대학교 예술공학부 캡스톤 디자인 프로젝트 2 전시용 파이프라인입니다.

사용자의 그림자를 캡처해 2D OBJ mesh로 만들고, Unity에서 해당 mesh를 표시한 뒤 MediaPipe 손 입력으로 윤곽선 vertex를 잡아 변형합니다. 변형이 끝나면 Unity가 PNG를 저장하고 SF3D worker API로 보내 texture PNG와 GLB 모델 생성을 요청합니다.

## Target Exhibition Setup

- OS: Windows 10/11
- GPU: NVIDIA CUDA 환경
- Unity: 2022.3 LTS
- Python: 3.11 권장
- Camera: USB 웹캠 2대
- Camera 0: 그림자 캡처용
- Camera 1: MediaPipe 손 추적용

Unity는 Windows에서 Python backend를 직접 실행하지 않고, `run_exhibition_windows.ps1`가 캡처, MediaPipe, SF3D 서버를 실행합니다. Unity는 `sf3d_io/live_shadow/`의 OBJ와 UDP `5052` 손 좌표를 기다립니다.

## Repository Layout

- `UnityProject/`: Unity 2022.3 프로젝트, 실행 씬은 `Assets/Scenes/ShadowPrototype.unity`
- `CAP_II/`: 그림자 캡처 및 OBJ 생성, 실행 파일은 `shadow_capture.py`
- `3d Hand Tracking/`: MediaPipe Hand Landmarker 손 추적, 실행 파일은 `main.py`
- `tools/list_cameras.py`: Windows/macOS OpenCV 카메라 번호 확인 도구
- `run_exhibition_windows.ps1`: Windows CUDA + USB 웹캠 전시 런처

## Windows Quick Start

PowerShell에서 repository 폴더로 이동한 뒤 실행합니다.

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\run_exhibition_windows.ps1 -CaptureCamera 0 -HandCamera 1 -Sf3dWorkerDir "C:\Users\<User>\Downloads\sf3d_worker"
```

실행 흐름은 아래 순서입니다.

1. SF3D API 서버가 별도 PowerShell 창에서 실행됩니다.
2. 그림자 캡처 창이 먼저 뜹니다.
3. 오브젝트 없는 상태에서 `Space`로 배경을 캡처합니다.
4. 오브젝트를 놓고 `Space`를 다시 눌러 그림자를 캡처합니다.
5. `CAP_II/output/shadow_mesh.obj`가 생성되고 Unity watch folder로 복사됩니다.
6. MediaPipe 손 추적 창이 별도 PowerShell 창에서 실행됩니다.
7. Unity 2022.3에서 `ShadowPrototype` 씬을 열고 Play를 누르면 mesh가 로드됩니다.
8. 손으로 vertex를 grab/pull 하며 변형합니다.
9. Unity에서 `Enter` 또는 `S`를 누르면 `deformed_shadow.png`가 저장되고 SF3D API로 전송됩니다.
10. 결과물은 `UnityProject/sf3d_io/sf3d_outputs/`에 저장됩니다.

SF3D worker를 아직 설치하지 않았거나 CUDA 의존성 설치가 오래 걸릴 때는 첫 실행 시간이 길 수 있습니다.

## Camera Index Check

웹캠 번호가 헷갈리면 먼저 카메라 스캔을 실행합니다.

```powershell
.\CAP_II\.venv\Scripts\python.exe .\tools\list_cameras.py --preview
```

아직 venv가 없으면 `run_exhibition_windows.ps1`를 한 번 실행해 requirements를 설치한 뒤 다시 카메라 스캔을 하면 됩니다.

카메라가 반대로 잡히면 실행 인자만 바꿉니다.

```powershell
.\run_exhibition_windows.ps1 -CaptureCamera 1 -HandCamera 0 -Sf3dWorkerDir "C:\Users\<User>\Downloads\sf3d_worker"
```

## SF3D Worker

Unity는 아래 API를 기대합니다.

- `POST http://127.0.0.1:8000/generate-texture`
- `POST http://127.0.0.1:8000/generate-3d`

`run_exhibition_windows.ps1`는 `-Sf3dWorkerDir` 또는 환경변수 `SF3D_WORKER_DIR`에서 `app.py`를 찾습니다. 경로는 `sf3d_worker` 루트여도 되고, 내부 `sf3d_worker/app.py` 폴더여도 됩니다.

기본 CUDA PyTorch wheel은 `cu121` 인덱스를 사용합니다.

```powershell
.\run_exhibition_windows.ps1 -TorchIndexUrl "https://download.pytorch.org/whl/cu121"
```

`xformers`는 버전 매칭 실패가 잦아서 기본 설치에서 제외했습니다. 꼭 필요하면 `-InstallXformers`를 붙입니다.

## Unity Output

Unity에서 `Enter` 또는 `S`를 누르면 아래 흐름이 실행됩니다.

1. 변형된 실루엣 PNG 저장: `UnityProject/sf3d_io/live_shadow/deformed_shadow.png`
2. ControlNet texture preview 저장: `UnityProject/sf3d_io/sf3d_outputs/last_texture.png`
3. SF3D GLB 저장: `UnityProject/sf3d_io/sf3d_outputs/shadow_asteroid_YYYYMMDD_HHMMSS.glb`

현재 GLB는 파일 저장까지 연결되어 있습니다. Unity scene 안에 GLB를 다시 runtime import하려면 GLTFast 같은 runtime importer를 붙이는 다음 단계가 필요합니다.

## Interaction Model

현재 조작은 전시장에서 바로 이해하기 쉽게 `hover -> pinch -> grab -> drag` 흐름으로 고정했습니다.

1. 검지를 오브젝트 윤곽선 가까이 가져갑니다.
2. Unity가 가장 가까운 boundary vertex를 자동 선택합니다.
3. 선택된 vertex가 하이라이트되고 `PINCH TO GRAB` 안내가 보입니다.
4. 엄지와 검지를 pinch 하면 선택된 vertex를 grab 합니다.
5. pinch 상태에서 손을 움직이면 선택 vertex 주변이 함께 부드럽게 당겨집니다.
6. 손을 펴면 release 됩니다.

오른쪽 세로 슬라이더는 감도가 아니라 `grab 영향 범위` 조절입니다. 슬라이더 근처에서 엄지와 중지를 pinch 하면 손으로도 영향 범위를 조절할 수 있습니다.

## Important Decisions

- 오브젝트 전체 이동은 사용하지 않습니다.
- rotation / scale 제어는 사용하지 않습니다.
- `tear` 조작은 사용하지 않습니다.
- 손 입력 감도는 중간 정도 고정값으로 유지합니다.
- 주변 vertex 영향 범위만 슬라이더로 조절합니다.

## Shadow Mesh Capture Notes

- `CAP_II/shadow_capture.py` 기본값은 `--epsilon 0.002`, `--spacing 8`, `--boundary-spacing 8`입니다.
- OpenCV 이미지 좌표계와 Unity 좌표계가 반대라서 OBJ 출력 시 기본적으로 Y축을 반전합니다.
- Y축 반전 시 mesh normal이 뒤집히지 않도록 face winding도 함께 반전합니다.
- 원본 이미지 방향 그대로 내보내고 싶으면 `--no-unity-flip-y`를 붙이면 됩니다.

## Debug Checklist

- Unity Console에 `Shadow mesh loaded`가 뜨면 OBJ 로드는 성공입니다.
- Unity Console에 `HandLandmarkUdpReceiver received first packet`이 뜨면 MediaPipe UDP 수신은 성공입니다.
- mesh는 보이는데 손 반응이 없으면 Windows 방화벽에서 UDP `5052`를 확인합니다.
- 카메라 창이 안 뜨면 다른 앱이 웹캠을 잡고 있는지 확인하고 `tools/list_cameras.py --preview`로 번호를 다시 확인합니다.
- Unity 2022.3 첫 실행 시 `packages-lock.json`은 자동 재생성됩니다.

## Requirements Files

- `CAP_II/requirements.txt`
- `3d Hand Tracking/requirements.txt`
- SF3D worker 쪽 requirements는 worker 폴더의 `requirements.txt`, `requirements_api.txt`를 사용합니다.

## Key Unity Runtime Scripts

- `UnityProject/Assets/Scripts/Runtime/ExhibitionFlowController.cs`
- `UnityProject/Assets/Scripts/Runtime/LiveMeshLoader.cs`
- `UnityProject/Assets/Scripts/Runtime/ShadowDeformer.cs`
- `UnityProject/Assets/Scripts/Runtime/MediaPipeMeshDeformationInput.cs`
- `UnityProject/Assets/Scripts/Runtime/MediaPipeInteractionVisualizer.cs`
- `UnityProject/Assets/Scripts/Runtime/DeformationControlPanel.cs`
- `UnityProject/Assets/Scripts/Runtime/Sf3dPngPipelineClient.cs`
- `UnityProject/Assets/Scripts/Runtime/PrototypeBootstrap.cs`

Unity 캐시 폴더(`Library`, `Temp`, `Logs`, `UserSettings`)는 git에 포함하지 않습니다.
