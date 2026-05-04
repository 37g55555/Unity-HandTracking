# Windows 실행 전 체크리스트

이 문서는 `window_webcam` 브랜치 기준입니다. 전체 파이프라인 실행 전에 아래 항목을 먼저 확인하세요.

## 1. 현재 폴더와 브랜치 확인

PowerShell에서 반드시 repository 루트로 이동합니다. `CAP_II` 폴더 안에서 실행하면 경로가 꼬입니다.

```powershell
cd C:\path\to\Unity-HandTracking
dir .\run_exhibition_windows.ps1
dir .\tools\list_cameras.py
```

`tools\list_cameras.py`가 없으면 브랜치가 잘못된 상태입니다.

```powershell
git fetch origin
git checkout window_webcam
git pull
```

확인:

```powershell
git branch --show-current
```

결과가 `window_webcam`이어야 합니다.

## 2. 필수 설치 확인

- Unity Hub
- Unity 2022.3 LTS, 권장 버전은 `2022.3.62f1`
- Python 3.11 64-bit, 설치 시 `Add python.exe to PATH` 체크
- Git
- NVIDIA GPU driver
- USB 웹캠 2대
- SF3D worker 폴더

CUDA Toolkit은 꼭 별도 설치하지 않아도 됩니다. 이 프로젝트의 PowerShell 스크립트는 기본적으로 CUDA PyTorch wheel을 설치합니다. 중요한 건 NVIDIA driver가 정상 설치되어 있는지입니다.

## 3. Windows 카메라 권한 확인

Windows 설정에서 확인합니다.

```text
설정 > 개인정보 및 보안 > 카메라
```

아래 항목을 켭니다.

- 카메라 액세스
- 데스크톱 앱에서 카메라에 액세스하도록 허용

카메라 앱, Zoom, OBS, Unity 등 다른 프로그램이 웹캠을 이미 잡고 있으면 Python에서 카메라가 안 열릴 수 있습니다.

## 4. 카메라 번호 확인

먼저 그림자 캡처용 / 손 추적용 카메라 번호를 확인합니다.

처음이라 `CAP_II\.venv`가 없으면 먼저 만듭니다.

```powershell
py -3.11 -m venv .\CAP_II\.venv
.\CAP_II\.venv\Scripts\python.exe -m pip install --upgrade pip
.\CAP_II\.venv\Scripts\python.exe -m pip install -r .\CAP_II\requirements.txt
```

그 다음 카메라 preview를 실행합니다.

```powershell
.\CAP_II\.venv\Scripts\python.exe .\tools\list_cameras.py --preview
```

예상 결과:

```text
[OK] camera 0: backend=DirectShow, size=1280x720 ...
[OK] camera 1: backend=DirectShow, size=1280x720 ...
```

보통은 아래처럼 둡니다.

- `CaptureCamera 0`: 그림자 캡처용 웹캠
- `HandCamera 1`: MediaPipe 손 추적용 웹캠

반대로 나오면 실행할 때 두 번호를 바꾸면 됩니다.

## 5. SF3D worker 경로 확인

SF3D worker 폴더 안에 `app.py`가 있어야 합니다.

가능한 형태:

```text
C:\Users\<User>\Downloads\sf3d_worker\app.py
```

또는:

```text
C:\Users\<User>\Downloads\sf3d_worker\sf3d_worker\app.py
```

확인 명령 예시:

```powershell
dir "C:\Users\<User>\Downloads\sf3d_worker\sf3d_worker\app.py"
```

경로가 다르면 전체 실행 명령의 `-Sf3dWorkerDir` 값을 실제 경로로 바꿔야 합니다.

## 6. PowerShell 실행 권한

PowerShell을 열 때마다 한 번 실행합니다.

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

이 명령은 현재 PowerShell 창에만 적용됩니다.

## 7. Unity 프로젝트 확인

Unity Hub에서 아래 폴더를 프로젝트로 엽니다.

```text
Unity-HandTracking\UnityProject
```

Unity 버전은 `2022.3 LTS`로 엽니다. 첫 실행 때 `packages-lock.json`이 재생성되면서 시간이 걸릴 수 있습니다.

실행 씬:

```text
Assets/Scenes/ShadowPrototype.unity
```

Play는 전체 파이프라인 실행 후 눌러도 되고, 먼저 눌러둬도 됩니다. Windows에서는 Unity가 Python을 직접 실행하지 않고 `sf3d_io/live_shadow`의 OBJ와 UDP 손 좌표를 기다립니다.

## 8. 전체 실행 명령

카메라 번호와 SF3D worker 경로를 확인한 뒤 실행합니다.

```powershell
.\run_exhibition_windows.ps1 -CaptureCamera 0 -HandCamera 1 -Sf3dWorkerDir "C:\Users\<User>\Downloads\sf3d_worker"
```

SF3D 없이 카메라/Unity 연결만 먼저 보고 싶으면:

```powershell
.\run_exhibition_windows.ps1 -CaptureCamera 0 -HandCamera 1 -SkipSf3d
```

카메라가 반대로 잡히면:

```powershell
.\run_exhibition_windows.ps1 -CaptureCamera 1 -HandCamera 0 -Sf3dWorkerDir "C:\Users\<User>\Downloads\sf3d_worker"
```

## 9. 정상 실행 순서

1. SF3D API 서버 PowerShell 창이 열립니다.
2. 그림자 캡처 창이 뜹니다.
3. 오브젝트 없는 배경에서 `Space`를 누릅니다.
4. 오브젝트를 놓고 그림자가 생기면 다시 `Space`를 누릅니다.
5. `UnityProject\sf3d_io\live_shadow\shadow_mesh.obj`가 생성됩니다.
6. MediaPipe 손 추적 창이 뜹니다.
7. Unity에서 `ShadowPrototype` 씬 Play를 누릅니다.
8. Unity Console에 `Shadow mesh loaded`가 뜨면 OBJ 로드 성공입니다.
9. Unity Console에 `HandLandmarkUdpReceiver received first packet`이 뜨면 손 좌표 수신 성공입니다.
10. 손으로 vertex를 grab/pull 해서 변형합니다.
11. Unity에서 `Enter` 또는 `S`를 누르면 PNG 저장 후 SF3D로 전송됩니다.

## 10. 자주 막히는 지점

`tools\list_cameras.py`가 없다면 `window_webcam` 브랜치가 아닙니다.

`CAP_II\.venv\Scripts\python.exe`가 없다면 venv를 아직 만들지 않은 상태입니다.

카메라 preview가 검은 화면이면 다른 앱이 카메라를 사용 중인지 확인합니다.

Unity에서 mesh가 안 뜨면 아래 파일이 있는지 확인합니다.

```powershell
dir .\UnityProject\sf3d_io\live_shadow\shadow_mesh.obj
dir .\UnityProject\sf3d_io\live_shadow\shadow_metadata.json
```

손 반응이 없으면 Windows 방화벽에서 UDP `5052` 수신을 허용하거나, Unity Console에서 UDP 첫 packet 로그가 뜨는지 확인합니다.
