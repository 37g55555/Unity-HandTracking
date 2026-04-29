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
6. 손 입력으로 캡처된 오브젝트를 변형
   - push
   - pull
   - tear

## Key Unity Runtime Scripts

- `UnityProject/Assets/Scripts/Runtime/ExhibitionFlowController.cs`
- `UnityProject/Assets/Scripts/Runtime/LiveMeshLoader.cs`
- `UnityProject/Assets/Scripts/Runtime/ShadowDeformer.cs`
- `UnityProject/Assets/Scripts/Runtime/MediaPipeMeshDeformationInput.cs`
- `UnityProject/Assets/Scripts/Runtime/MediaPipeInteractionVisualizer.cs`

## Notes

- Unity 캐시 폴더(`Library`, `Temp`, `Logs`, `UserSettings`)는 제외되어 있습니다.
- `CAP_II/output/`에는 샘플로 최근 생성된 OBJ/metadata/output image가 포함되어 있습니다.
- `3d Hand Tracking/hand_landmarker.task`가 포함되어 있어 바로 MediaPipe 손 추적을 실행할 수 있습니다.
