## Setup

기준 실행 환경: Windows PC 1대, 웹캠 2대

```powershell
# 저장소 클론
git clone https://github.com/37g55555/Unity-HandTracking.git
cd Unity-HandTracking

# Conda 가상환경 생성
conda env create -f environment.yml
conda activate artifact

# Hugging Face
huggingface-cli login
```


## Project Structure

```text
Unity-HandTracking
├─ UnityProject
│  ├─ Assets
│  │  ├─ Scenes
│  │  │  ├─ Main.unity
│  │  │  └─ hologramOut.unity
│  │  └─ Scripts
│  ├─ Packages
│  └─ ProjectSettings
├─ python
│  ├─ ShadowMesh.py
│  ├─ MediaPipeTracking.py
│  └─ MediaPipe.task
├─ sf3d
│  ├─ app.py
│  ├─ silhouette_labeler.py
│  ├─ sf3d
│  ├─ texture_baker
│  └─ uv_unwrapper
└─ output
   ├─ shadowmesh
   ├─ sf3d
   └─ recordings
```
`output\sf3d`, `output\recordings` 폴더는 실행 시 자동으로 생성된다.

### 모델 파일 확인

Qwen VLM 모델은 `sf3d/app.py` 실행 중 Hugging Face/transformers를 통해 로드된다.  
SF3D와 ControlNet 모델은 `sf3d/app.py` 실행 중 Hugging Face/diffusers를 통해 로드된다.

### Hugging Face 인증

실행 전 가상환경에서 Hugging Face 로그인을 완료한다.  
토큰은 Hugging Face의 Settings > Access Tokens에서 Read 권한으로 생성한다.
```text
https://huggingface.co/settings/tokens
```

아래 모델 페이지에서 필요한 경우 접근 약관을 수락한다.
```text
https://huggingface.co/stabilityai/stable-fast-3d
https://huggingface.co/Qwen/Qwen2.5-VL-3B-Instruct
https://huggingface.co/lllyasviel/sd-controlnet-canny
https://huggingface.co/runwayml/stable-diffusion-v1-5
```

### Unity 프로젝트 열기

Unity Hub에서 `UnityProject` 폴더를 프로젝트로 연다.


## Execution Flow

1. 웹캠 2대를 PC에 연결
2. `conda activate artifact`로 가상환경을 활성화
3. Unity에서 `UnityProject/Assets/Scenes/Main.unity`를 열기
4. Inspector 경로가 현재 PC 경로와 맞는지 확인
5. Unity Play를 실행


## Scripts Overview

### Unity Scripts

| Script | 역할 |
| --- | --- |
| `GameStateManager.cs` | 파이프라인 상태 관리 및 상태 변경 로그 출력. |
| `PipelineManager.cs` | 전체 전시 파이프라인 제어. SF3D 서버 실행, ShadowMesh 실행, MediaPipe 실행, Enter 입력 기반 SF3D 요청을 담당. |
| `ObjParser.cs` | ShadowMesh OBJ 파일을 Unity Mesh로 파싱. |
| `ShadowMeshFileLoader.cs` | `output/shadowmesh/shadow_mesh.obj`와 `shadow_metadata.json`를 감지해 Unity 메쉬로 불러옴. |
| `ShadowMeshRootController.cs` | ShadowMesh 루트 오브젝트의 기본 transform 제어를 담당. |
| `ShadowMeshDeformer.cs` | 그림자 메쉬 표시, 변형, 실루엣 PNG 추출을 담당. |
| `ShadowMeshDeformationSlider.cs` | 그림자 변형 반경을 조절하는 슬라이더 UI. |
| `MediaPipeUdpReceiver.cs` | Python MediaPipe 스크립트가 보내는 UDP 손 좌표를 수신. |
| `MediaPipeMeshDeformationInput.cs` | MediaPipe 손 좌표를 그림자 메쉬 변형 입력으로 변환. |
| `MediaPipeInteractionVisualizer.cs` | 손 입력, hover, grab 상태를 시각화. |
| `SF3DGenerationClient.cs` | SF3D FastAPI 서버에 texture/model 생성 요청을 보내고 GLB 결과를 저장. |
| `HologramDisplayManager.cs` | 홀로그램 출력 씬에서 표시 관련 제어를 담당. |
| `HologramModelRecorder.cs` | 생성된 GLB를 불러오고 회전/녹화 출력을 담당. |

### Python Scripts

| Script | 역할 |
| --- | --- |
| `python/ShadowMesh.py` | 웹캠으로 배경/그림자를 캡처해 2D shadow mesh를 생성. |
| `python/MediaPipeTracking.py` | 웹캠 손 추적 결과를 UDP로 Unity에 전송. |
| `sf3d/app.py` | FastAPI 서버. Qwen 기반 실루엣 분류, ControlNet 기반 texture 생성, SF3D 기반 GLB 생성을 담당. |
| `sf3d/silhouette_labeler.py` | `Qwen/Qwen2.5-VL-3B-Instruct`로 그림자 실루엣과 가장 닮은 동물/사물 이름을 한 단어로 추론. |


## Unity Inspector Settings

현재 기본 경로는 아래 로컬 경로를 기준으로 한다.

```text
D:\Unity-HandTracking
```

다른 PC나 다른 폴더에서 실행할 경우 아래 경로 값을 실제 설치 경로에 맞게 수정해야 한다.

### Main.unity

#### `PipelineManager`

| Field | 값 | 설명 |
| --- | --- | --- |
| `pythonExecutablePath` | `D:\anaconda3\envs\artifact\python.exe` | Python 실행 파일 경로 |
| `sf3dWorkingDirectory` | `D:\Unity-HandTracking\sf3d` | SF3D 서버 실행 위치 |
| `captureWorkingDirectory` | `D:\Unity-HandTracking` | ShadowMesh 실행 기준 경로 |
| `handTrackingWorkingDirectory` | `D:\Unity-HandTracking` | MediaPipe 실행 기준 경로 |

`captureArguments`를 아래처럼 바꾸면 웹캠 캡처 없이 기존 shadow mesh 파일을 사용한다.

```text
--mode file
```

#### `ShadowMeshFileLoader`

| Field | 현재 값 | 설명 |
| --- | --- | --- |
| `absoluteWatchDirectoryOverride` | `D:\Unity-HandTracking\output\shadowmesh` | ShadowMesh 결과 파일을 읽는 폴더 |

### hologramOut.unity

#### `HologramModelRecorder`

| Field | 현재 값 | 설명 |
| --- | --- | --- |
| `inputDirectory` | `D:/Unity-HandTracking/output/sf3d` | 불러올 GLB 폴더 |
| `outputDirectory` | `D:/Unity-HandTracking/output/recordings` | 녹화 결과 저장 폴더 |


## Pipeline States

상태는 `GameStateManager.PipelineState`에서 관리한다.

| Initial State | 위치 | 입력 키 | 동작 | Target State |
| --- | --- | --- | --- | --- |
| `Idle` | ShadowMesh 웹캠 창 | Enter | 배경 캡처 | `ShadowCapturing` |
| `ShadowCapturing` | ShadowMesh 웹캠 창 | Enter | 그림자 캡처 | `MediaPipeTracking` |
| `MediaPipeTracking` | Unity Main 씬 | Enter | 관객이 그림자를 변형하는 동안 Qwen 실루엣 분류를 백그라운드로 진행하고, Enter 입력 시 메쉬 추출 | `MeshExtracting` |
| `MeshExtracting` | Unity Main 씬 | - | 변형된 그림자 PNG 추출 | `Reconstructing3D` |
| `Reconstructing3D` | Unity Main 씬 | - | SF3D 3D 재구성 진행 | `HologramOutput` |
| `HologramOutput` | Unity hologramOut 씬 | - | GLB 생성 완료 및 홀로그램 출력 | `Idle` |


## Outputs

### ShadowMesh 출력

위치:

```text
output\shadowmesh
```

| 파일 | 설명 |
| --- | --- |
| `shadow_contour.png` | 추출된 그림자 윤곽 확인용 이미지 (실사용 X) |
| `shadow_mesh.obj` | Unity가 불러오는 2D 그림자 메쉬 |
| `shadow_metadata.json` | boundary index, scale, center offset 등 메쉬 보정 정보 |

### SF3D 출력

위치:

```text
output\sf3d
```

| 파일 | 설명 |
| --- | --- |
| `deformed_shadow.png` | Unity에서 추출한 변형 그림자 실루엣 |
| `last_texture.png` | ControlNet으로 생성된 texture preview |
| `shadow_asteroid_*.glb` | SF3D로 생성된 3D 모델 |

### Hologram Recording 출력

위치:

```text
output\recordings
```

| 파일 | 설명 |
| --- | --- |
| `n.mp4` | 홀로그램 영상 |


## Troubleshooting

### 웹캠이 열리지 않을 때

- 웹캠 2대가 모두 연결되어 있는지 확인한다.
- 다른 프로그램이 웹캠을 사용 중인지 확인한다.
- ShadowMesh용 카메라 번호: `captureArguments`의 `--camera 0`
- MediaPipe용 카메라 번호: `handTrackingArguments`의 `--camera 1`
- 두 카메라가 반대로 잡히면 두 값을 서로 바꾼다.

예:

```text
captureArguments: --mode live --camera 1
handTrackingArguments: --camera 0
```

### SF3D 서버가 안 켜질 때

- `conda activate artifact`가 되어 있는지 확인한다.
- Hugging Face 모델 접근 권한이 필요한 경우 해당 환경에서 먼저 로그인한다.


### GLB가 안 생성될 때

- `output\sf3d` 폴더가 생성되는지 확인한다.
- SF3D 서버 콘솔에서 texture/model generation 오류가 없는지 확인한다.
- Unity Console에서 `SF3DGenerationClient` 경고를 확인한다.

## Git Ignore / Generated Files

아래 파일과 폴더는 실행 중 생성되는 산출물이므로 git에 올리지 않도록 한다.

```text
UnityProject/Library/
UnityProject/Temp/
UnityProject/Logs/
UnityProject/UserSettings/
UnityProject/obj/
UnityProject/.vs/
UnityProject/.vsconfig
UnityProject/*.csproj
UnityProject/*.sln
output/sf3d/
output/recordings/
__pycache__/
*.pyc
```

Unity 프로젝트를 열면 `.csproj`, `.sln`, `Library`, `Temp` 등은 다시 생성됨
