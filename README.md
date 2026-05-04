중앙대학교 예술공학부 캡스톤 디자인 프로젝트 2

## Repository Layout

- `UnityProject/`
  - 전시용 Unity 프로젝트
  - `Assets/Scenes/ShadowPrototype.unity`에서 실행
- `CAP_II/`
  - 그림자 캡처 및 OBJ 생성 파이프라인
  - `shadow_capture.py`
- `3d Hand Tracking/`
  - MediaPipe Hand Landmarker 기반 손 추적
  - `main.py`

## Exhibition Flow

1. Unity에서 `ShadowPrototype` 씬을 열고 Play
2. Unity가 `CAP_II/shadow_capture.py`를 자동 실행
3. 그림자 캡처 후 `shadow_mesh.obj`가 생성됨
4. Unity가 OBJ를 런타임 로드
5. Unity가 MediaPipe 손 추적을 자동 실행
6. 손 입력으로 캡처된 오브젝트의 윤곽선 vertex를 선택하고 변형
7. Enter 또는 S 키를 누르면 변형된 실루엣이 `deformed_shadow.png`로 저장됨
8. Unity가 저장된 PNG를 SF3D worker API에 보내 texture PNG와 GLB 모델을 생성

## SF3D 연결

SF3D worker는 별도 터미널에서 먼저 켜둡니다.

```bash
./run_sf3d_worker_mac.sh
```

macOS에서는 `xformers`가 clang 빌드 에러를 내는 경우가 있어, 위 스크립트가 자동으로 `xformers`만 제외하고 requirements를 설치합니다. 이 의존성은 CUDA 최적화용이라 Mac/MPS 실행에는 필수로 쓰지 않습니다.

기본 경로는 아래 폴더를 사용합니다.

```text
~/Downloads/sf3d_worker/sf3d_worker
```

다른 위치에 worker가 있으면:

```bash
SF3D_WORKER_DIR="/path/to/sf3d_worker/sf3d_worker" ./run_sf3d_worker_mac.sh
```

Unity 쪽 흐름:

1. `deformed_shadow.png`가 `~/Downloads/CAP_II/output/`에 저장됩니다.
2. `Sf3dPngPipelineClient`가 해당 PNG를 `http://127.0.0.1:8000/generate-texture`로 보냅니다.
3. worker가 ControlNet texture PNG를 반환하면 Unity가 `UnityProject/sf3d_io/sf3d_outputs/last_texture.png`로 저장합니다.
4. Unity가 texture PNG를 `http://127.0.0.1:8000/generate-3d`로 다시 보냅니다.
5. worker가 반환한 GLB는 `UnityProject/sf3d_io/sf3d_outputs/shadow_asteroid_YYYYMMDD_HHMMSS.glb`로 저장됩니다.

즉 Unity에서 Enter/S 키를 누르는 순간 `변형된 PNG -> ControlNet texture -> SF3D GLB`까지 이어집니다. SF3D 모델 로딩은 아직 파일 저장까지이며, 런타임 GLB 씬 로딩은 GLTFast 같은 별도 런타임 importer를 붙이는 다음 단계입니다.

## Current Interaction Model

현재 조작은 복잡한 제스처를 줄이고, 전시에서 바로 이해할 수 있는 방식으로 단순화되어 있습니다.

1. 손 검지를 오브젝트 윤곽선 가까이 가져갑니다.
2. Unity가 가장 가까운 boundary vertex를 자동으로 snap 선택합니다.
3. 선택된 vertex가 하이라이트되고 `PINCH TO GRAB` 안내가 보입니다.
4. 엄지와 검지를 pinch 하면 선택된 vertex를 grab 합니다.
5. pinch 상태에서 손을 움직이면 해당 지점을 부드럽게 pull deformation 합니다.
6. 손을 펴면 release 됩니다.

Game view 오른쪽의 세로 슬라이더로 grab 된 vertex 주변의 영향 범위를 실시간 조절할 수 있습니다. 손으로 당기는 감도는 중간 정도의 고정값으로 유지되며, 슬라이더 근처에서 엄지와 중지를 pinch 하면 MediaPipe 손 입력으로도 슬라이더를 잡고 조절할 수 있습니다.

### Important

- 오브젝트 전체 이동은 사용하지 않습니다.
- rotation / scale 제어는 사용하지 않습니다.
- `tear` 기능은 제거되었습니다.
- 현재는 `hover -> pinch -> grab -> drag` 흐름으로만 조작합니다.

## Visual Feedback In Unity

Unity Game view 안에서 아래 정보가 바로 보이도록 구성되어 있습니다.

- 손 검지 위치 마커
- 엄지 위치 마커
- 현재 선택된 boundary vertex 하이라이트
- 손가락과 선택 vertex 사이 연결선
- `PINCH TO GRAB` / `GRAB` 상태 텍스트
- 현재 deformation 영향 범위 표시 링
- 주변 vertex 영향 범위 조절용 오른쪽 세로 슬라이더

즉 MediaPipe 창을 보지 않아도 Unity 화면만 보고 어느 점이 선택됐는지 확인할 수 있습니다.

## Shadow Mesh Capture Notes

- `CAP_II/shadow_capture.py`는 기본적으로 `--epsilon 0.002`, `--spacing 8`, `--boundary-spacing 8`을 사용해 이전보다 촘촘한 mesh를 생성합니다.
- OpenCV 이미지 좌표계와 Unity 좌표계가 반대라서 OBJ 출력 시 기본적으로 Y축을 반전합니다.
- 원본 이미지 방향 그대로 내보내고 싶을 때는 `--no-unity-flip-y`를 붙이면 됩니다.

## Camera Notes

- macOS에서 카메라 장치 순서가 바뀌는 문제를 줄이기 위해
  `shadow_capture.py`와 `3d Hand Tracking/main.py` 모두
  여러 camera index와 AVFoundation/default backend를 자동 재시도합니다.
- 아이폰 연속성 카메라가 우선 잡히는 경우,
  iPhone의 `연속성 카메라`를 끄는 것을 권장합니다.

## Key Unity Runtime Scripts

- `UnityProject/Assets/Scripts/Runtime/ExhibitionFlowController.cs`
- `UnityProject/Assets/Scripts/Runtime/LiveMeshLoader.cs`
- `UnityProject/Assets/Scripts/Runtime/ShadowDeformer.cs`
- `UnityProject/Assets/Scripts/Runtime/MediaPipeMeshDeformationInput.cs`
- `UnityProject/Assets/Scripts/Runtime/MediaPipeInteractionVisualizer.cs`
- `UnityProject/Assets/Scripts/Runtime/Sf3dPngPipelineClient.cs`
- `UnityProject/Assets/Scripts/Runtime/PrototypeBootstrap.cs`

## Main Runtime Responsibilities

- `ExhibitionFlowController.cs`
  - 전시 흐름 자동 실행
  - 그림자 캡처 실행
  - OBJ 생성 후 손 추적 실행
- `LiveMeshLoader.cs`
  - 새롭게 생성된 OBJ / metadata 감시 및 로드
- `ShadowDeformer.cs`
  - mesh runtime 교체
  - boundary vertex 조회
  - deformation 적용
- `MediaPipeMeshDeformationInput.cs`
  - MediaPipe 손 랜드마크를 Unity mesh local 좌표로 투영
  - 윤곽선 vertex 선택 / grab / pull 처리
- `MediaPipeInteractionVisualizer.cs`
  - Unity Game view 안의 조작 가이드 시각화
- `Sf3dPngPipelineClient.cs`
  - Unity에서 저장된 `deformed_shadow.png`를 SF3D worker API에 전송
  - ControlNet texture preview와 SF3D GLB 결과 저장

## Notes

- Unity 캐시 폴더(`Library`, `Temp`, `Logs`, `UserSettings`)는 제외되어 있습니다.
- `CAP_II/output/`에는 샘플로 최근 생성된 OBJ/metadata/output image가 포함되어 있습니다.
- `3d Hand Tracking/hand_landmarker.task`가 포함되어 있어 바로 MediaPipe 손 추적을 실행할 수 있습니다.
- 실제 실행 씬은 `UnityProject/Assets/Scenes/ShadowPrototype.unity` 입니다.
