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

## Current Interaction Model

현재 조작은 복잡한 제스처를 줄이고, 전시에서 바로 이해할 수 있는 방식으로 단순화되어 있습니다.

1. 손 검지를 오브젝트 윤곽선 가까이 가져갑니다.
2. Unity가 가장 가까운 boundary vertex를 자동으로 snap 선택합니다.
3. 선택된 vertex가 하이라이트되고 `PINCH TO GRAB` 안내가 보입니다.
4. 엄지와 검지를 pinch 하면 선택된 vertex를 grab 합니다.
5. pinch 상태에서 손을 움직이면 해당 지점을 부드럽게 pull deformation 합니다.
6. 손을 펴면 release 됩니다.

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
- 현재 deformation 반경 표시 링

즉 MediaPipe 창을 보지 않아도 Unity 화면만 보고 어느 점이 선택됐는지 확인할 수 있습니다.

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

## Notes

- Unity 캐시 폴더(`Library`, `Temp`, `Logs`, `UserSettings`)는 제외되어 있습니다.
- `CAP_II/output/`에는 샘플로 최근 생성된 OBJ/metadata/output image가 포함되어 있습니다.
- `3d Hand Tracking/hand_landmarker.task`가 포함되어 있어 바로 MediaPipe 손 추적을 실행할 수 있습니다.
- 실제 실행 씬은 `UnityProject/Assets/Scenes/ShadowPrototype.unity` 입니다.
