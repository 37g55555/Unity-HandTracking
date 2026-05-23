## Setup

기준 실행 환경: Windows PC 1대, 웹캠 2대, Visual Studio 2022 C++ Build Tools

```powershell
git clone https://github.com/37g55555/Unity-HandTracking.git
cd Unity-HandTracking

conda env create -f environment.yml
conda activate artifact

# Visual Studio 2022 C++ Build Tools
if exist "%CONDA_PREFIX%\etc\conda\activate.d\vs2017_compiler_vars.bat" ren "%CONDA_PREFIX%\etc\conda\activate.d\vs2017_compiler_vars.bat" vs2017_compiler_vars.bat.disabled
if exist "%CONDA_PREFIX%\etc\conda\activate.d\vs2017_get_vsinstall_dir.bat" ren "%CONDA_PREFIX%\etc\conda\activate.d\vs2017_get_vsinstall_dir.bat" vs2017_get_vsinstall_dir.bat.disabled
conda activate artifact
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
where cl

#Build SF3D local extension
python -m pip install --no-build-isolation ./sf3d/texture_baker ./sf3d/uv_unwrapper

# Hugging Face
hf auth login

hf download Qwen/Qwen2.5-VL-3B-Instruct
hf download stabilityai/stable-fast-3d
hf download lllyasviel/sd-controlnet-canny
hf download stable-diffusion-v1-5/stable-diffusion-v1-5
```

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
https://huggingface.co/Qwen/Qwen2.5-VL-3B-Instruct
https://huggingface.co/stabilityai/stable-fast-3d
https://huggingface.co/lllyasviel/sd-controlnet-canny
https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-v1-5
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
   └─ sf3d
```
`output\sf3d` 폴더는 실행 시 자동으로 생성된다.

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
| `MediaPipeUdpReceiver.cs` | Python MediaPipe 스크립트가 보내는 UDP 손 좌표를 수신. |
| `MediaPipeMeshDeformationInput.cs` | MediaPipe 손 좌표를 그림자 메쉬 변형 입력으로 변환. |
| `MediaPipeInteractionVisualizer.cs` | MediaPipe 손 입력 상태, 경계 마커, 손 그림자 실루엣 메쉬/outline 시각화. |
| `SF3DGenerationClient.cs` | SF3D FastAPI 서버에 texture/model 생성 요청을 보내고 GLB 결과를 저장. |
| `HologramSceneManager.cs` | hologramOut 씬에서 Enter 입력 시 Main 씬으로 돌아감. |
| `HologramModelLoader.cs` | 생성된 GLB를 불러오고 회전 표시를 담당. |

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

#### `HologramModelLoader`

| Field | 현재 값 | 설명 |
| --- | --- | --- |
| `inputDirectory` | `D:/Unity-HandTracking/output/sf3d` | 불러올 GLB 폴더 |

### MediaPipe 설정 관련

#### MediaPipeMeshDeformationInput - Grab Gesture

| Field | 설명 |
| --- | --- |
| `Hover Snap Distance Local` | 검지 위치가 그림자 메쉬 경계에 얼마나 가까워야 hover 대상으로 잡히는지 정한다. (Hover: 주황색 마커) 값이 크면 경계에서 조금 멀어도 잡을 수 있고, 값이 작으면 경계에 더 정확히 접근해야 한다. |
| `Pinch Enter Threshold Pixels` | 엄지와 검지 사이 거리가 이 값 이하가 되면 grab 진입 조건으로 판단한다. (Grab/Pull: 초록색 마커) |
| `Pinch Exit Threshold Pixels` | grab 상태에서 엄지와 검지 사이 거리가 이 값보다 커지면 grab을 해제한다. |
| `Grab Activation Hold Seconds` | pinch 조건이 충족된 뒤 실제 grab으로 전환되기까지 유지해야 하는 시간이다. 값이 크면 실수로 스치듯 잡히는 상황이 줄고, 값이 작으면 반응이 빨라진다. |
| `Affected Radius Local` | grab으로 당길 때 주변 정점까지 함께 움직이는 영향 반경. Maya의 Soft Selection 기능과 비슷하다. 값이 크면 넓은 영역이 부드럽게 같이 움직이고, 값이 작으면 잡은 지점 근처만 강하게 변형된다. |
| `Pull Strength` | 손 움직임이 메쉬 변형에 반영되는 세기. 값이 크면 같은 손 움직임에도 더 빠르고 크게 당겨지고, 값이 작으면 더 천천히 부드럽게 변형된다. |

#### MediaPipeInteractionVisualizer - Hand Shadow

| Field | 설명 |
| --- | --- |
| `Show Hand Shadow` | MediaPipe 손 랜드마크 기반 손 그림자 실루엣 메쉬를 표시할지 정한다. |
| `Hand Shadow Color` | 손 그림자 실루엣 본체 색상과 투명도. |
| `Screen Hand Shadow Distance` | 손 그림자 실루엣을 카메라 앞 어느 거리에서 그릴지 정한다. 화면 기준으로 손 실루엣을 배치할 때 사용한다. |
| `Screen Hand Shadow Scale` | 손 그림자 실루엣 전체 크기 배율. |
| `Hand Shadow Finger Width Scale` | 손가락 segment 두께 배율. 손가락 막대 부분만 굵어지며, palm과 cap 크기는 별도 기준으로 고정임. |
| `Hand Shadow Outline Color` | 손 그림자 실루엣 뒤에 그리는 outline 메쉬 색상. |
| `Hand Shadow Outline Scale` | outline 메쉬를 본체보다 얼마나 크게 그릴지 정한다. 값이 클수록 흰색 외곽이 두껍게 보인다. |


## Pipeline States

상태는 `GameStateManager.PipelineState`에서 관리한다.

| Initial State | 위치 | 입력 키 | 동작 | Target State |
| --- | --- | --- | --- | --- |
| `Idle` | ShadowMesh 웹캠 창 | Enter | 배경 캡처 | `ShadowCapturing` |
| `ShadowCapturing` | ShadowMesh 웹캠 창 | Enter | 그림자 캡처 | `MediaPipeTracking` |
| `MediaPipeTracking` | Unity Main 씬 | Enter | 관객이 그림자를 변형하는 동안 Qwen 실루엣 분류를 백그라운드로 진행하고, Enter 입력 시 메쉬 추출 | `MeshExtracting` |
| `MeshExtracting` | Unity Main 씬 | - | 변형된 그림자 PNG 추출 | `Reconstructing3D` |
| `Reconstructing3D` | Unity Main 씬 | - | SF3D 3D 재구성 진행 | `HologramOutput` |
| `HologramOutput` | Unity hologramOut 씬 | Enter | GLB 생성 완료 및 홀로그램 출력, Enter 입력 시 Main 씬으로 복귀 | `Idle` |


## Outputs

### ShadowMesh 출력

위치:

```text
output\shadowmesh
```

| 파일 | 설명 |
| --- | --- |
| `shadow_contour.png` | 캡처된 그림자 윤곽 이미지 (Qwen 입력) |
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
| `shadow_model.glb` | SF3D로 생성된 3D 모델 |


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

- Hugging Face 모델 접근 권한이 필요한 경우 해당 환경에서 먼저 로그인한다.


### GLB가 안 생성될 때

- `output\sf3d` 폴더가 생성되는지 확인한다.
- SF3D 서버 콘솔에서 texture/model generation 오류가 없는지 확인한다.
- Unity Console에서 `SF3DGenerationClient` 경고를 확인한다.
