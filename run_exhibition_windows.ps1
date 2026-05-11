param(
    [int]$CaptureCamera = 0,
    [int]$HandCamera = 1,
    [int]$UnityUdpPort = 5053,
    [string]$UnityProjectPath = "$PSScriptRoot\UnityProject",
    [string]$Sf3dWorkerDir = "",
    [string]$TorchIndexUrl = "https://download.pytorch.org/whl/cu124",
    [switch]$UseIpCamera,
    [string]$IpCameraUrl = "http://192.168.0.12:8081/video",
    [switch]$UseRembgGpu,
    [switch]$InstallXformers,
    [switch]$SkipDependencyInstall,
    [switch]$SkipSf3d,
    [switch]$Sf3dServerOnly,
    [switch]$OpenUnity,
    [string]$UnityEditorPath = "",
    [int]$CameraWidth = 640,
    [int]$CameraHeight = 480,
    [int]$CameraFps = 15,
    [int]$HandCameraWidth = 320,
    [int]$HandCameraHeight = 240,
    [int]$HandCameraFps = 10,
    [float]$ShadowEpsilon = 0.002,
    [float]$ShadowSpacing = 8
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$CapDir = Join-Path $Root "CAP_II"
$HandDir = Join-Path $Root "3d Hand Tracking"
$LogDir = Join-Path $Root "logs"
$UnityWatchDir = Join-Path $UnityProjectPath "output"

function Write-Section {
    param([string]$Text)
    Write-Host ""
    Write-Host "============================================================"
    Write-Host $Text
    Write-Host "============================================================"
}

function Invoke-NativeCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function New-PythonVenv {
    param([string]$VenvDir)

    if (Get-Command py -ErrorAction SilentlyContinue) {
        Invoke-NativeCommand -FilePath "py" -Arguments @("-3.11", "-m", "venv", $VenvDir)
        if (Test-Path -LiteralPath (Resolve-VenvPython -VenvDir $VenvDir)) {
            return
        }
    }

    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCommand -and $pythonCommand.Source -notmatch "\\WindowsApps\\python\.exe$") {
        Invoke-NativeCommand -FilePath $pythonCommand.Source -Arguments @("-m", "venv", $VenvDir)
        if (Test-Path -LiteralPath (Resolve-VenvPython -VenvDir $VenvDir)) {
            return
        }
    }

    $condaCandidates = @(
        (Join-Path $HOME "miniconda3\Scripts\conda.exe"),
        (Join-Path $HOME "anaconda3\Scripts\conda.exe")
    )

    $condaCommand = Get-Command conda -ErrorAction SilentlyContinue
    if ($condaCommand) {
        $condaCandidates = @($condaCommand.Source) + $condaCandidates
    }

    foreach ($condaPath in $condaCandidates) {
        if (Test-Path -LiteralPath $condaPath) {
            Invoke-NativeCommand -FilePath $condaPath -Arguments @("create", "-y", "-p", $VenvDir, "python=3.11", "pip")
            if (Test-Path -LiteralPath (Resolve-VenvPython -VenvDir $VenvDir)) {
                return
            }
        }
    }

    throw "Python was not found. Install Python 3.11 or Miniconda."
}

function Resolve-VenvPython {
    param([string]$VenvDir)

    foreach ($candidate in @(
        (Join-Path $VenvDir "Scripts\python.exe"),
        (Join-Path $VenvDir "python.exe"),
        (Join-Path $VenvDir "bin\python")
    )) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return Join-Path $VenvDir "Scripts\python.exe"
}

function Test-MsvcBuildTools {
    $vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        return $false
    }

    $installPath = & $vswhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null |
        Select-Object -First 1
    return -not [string]::IsNullOrWhiteSpace($installPath)
}

function Ensure-Venv {
    param(
        [string]$Directory,
        [string]$RequirementsPath
    )

    $venvDir = Join-Path $Directory ".venv"
    $pythonPath = Resolve-VenvPython -VenvDir $venvDir

    if (-not (Test-Path -LiteralPath $pythonPath)) {
        Write-Host "[setup] Creating venv: $venvDir"
        New-PythonVenv -VenvDir $venvDir
        $pythonPath = Resolve-VenvPython -VenvDir $venvDir
    }

    if (-not $SkipDependencyInstall) {
        Write-Host "[setup] Installing requirements: $RequirementsPath"
        Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "--upgrade", "pip")
        Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "-r", $RequirementsPath)
    }

    return $pythonPath
}

function Resolve-Sf3dAppDirectory {
    param([string]$RequestedPath)

    if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        if ($env:SF3D_WORKER_DIR) {
            $RequestedPath = $env:SF3D_WORKER_DIR
        }
        elseif (Test-Path -LiteralPath (Join-Path $Root "sf3d_worker\sf3d_worker")) {
            $RequestedPath = Join-Path $Root "sf3d_worker\sf3d_worker"
        }
        elseif (Test-Path -LiteralPath (Join-Path $Root "sf3d_worker")) {
            $RequestedPath = Join-Path $Root "sf3d_worker"
        }
        else {
            $RequestedPath = Join-Path $HOME "Downloads\sf3d_worker"
        }
    }

    if (Test-Path -LiteralPath (Join-Path $RequestedPath "app.py")) {
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $nested = Join-Path $RequestedPath "sf3d_worker"
    if (Test-Path -LiteralPath (Join-Path $nested "app.py")) {
        return (Resolve-Path -LiteralPath $nested).Path
    }

    throw "SF3D worker app.py was not found. Pass -Sf3dWorkerDir `"C:\path\to\sf3d_worker`" or set SF3D_WORKER_DIR."
}

function Ensure-Sf3dVenv {
    param([string]$AppDir)

    $venvDir = Join-Path $AppDir ".venv"
    $pythonPath = Resolve-VenvPython -VenvDir $venvDir

    if (-not (Test-Path -LiteralPath $pythonPath)) {
        Write-Host "[setup] Creating SF3D venv: $venvDir"
        New-PythonVenv -VenvDir $venvDir
        $pythonPath = Resolve-VenvPython -VenvDir $venvDir
    }

    if ($SkipDependencyInstall) {
        return $pythonPath
    }

    Push-Location -LiteralPath $AppDir
    try {
        Write-Host "[setup] Installing SF3D CUDA torch wheels from $TorchIndexUrl"
        Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "--upgrade", "pip")
        Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "torch", "torchvision", "torchaudio", "--index-url", $TorchIndexUrl)

        $tempReq = Join-Path $AppDir ".sf3d_requirements_windows.tmp.txt"
        $requirementSources = @()
        foreach ($candidate in @("requirements.txt", "requirements_api.txt")) {
            $candidatePath = Join-Path $AppDir $candidate
            if (Test-Path -LiteralPath $candidatePath) {
                $requirementSources += $candidatePath
            }
        }

        if ($requirementSources.Count -gt 0) {
            Get-Content -LiteralPath $requirementSources |
                Where-Object {
                    $_ -notmatch "^\s*$" -and
                    $_ -notmatch "^\s*#" -and
                    $_ -notmatch "^\s*torch(\s|=|>|<|;|$)" -and
                    $_ -notmatch "^\s*torchvision(\s|=|>|<|;|$)" -and
                    $_ -notmatch "^\s*torchaudio(\s|=|>|<|;|$)" -and
                    $_ -notmatch "^\s*xformers(\s|=|>|<|;|$)" -and
                    $_ -notmatch "^\s*rembg(\[.*\])?(\s|=|>|<|;|$)" -and
                    $_ -notmatch "^\s*\./(texture_baker|uv_unwrapper)/?\s*$"
                } |
                Set-Content -LiteralPath $tempReq -Encoding UTF8

            Write-Host "[setup] Installing SF3D/API requirements, excluding torch/xformers/rembg duplicates"
            Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "-r", $tempReq)
        }

        if ($UseRembgGpu) {
            Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "numpy==1.26.4", "opencv-python-headless==4.11.0.86", "rembg[gpu]==2.0.57")
        }
        else {
            Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "numpy==1.26.4", "opencv-python-headless==4.11.0.86", "rembg==2.0.57")
        }

        if ($InstallXformers) {
            Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "xformers")
        }

        if (-not (Test-MsvcBuildTools)) {
            throw @"
SF3D native extensions require Microsoft C++ Build Tools, but the Visual Studio C++ toolchain is not installed.
This PC has Visual Studio, so the recommended fix is:
  Visual Studio Installer > Modify > Desktop development with C++ > Install

Or run this in an Administrator PowerShell:
  & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\setup.exe" modify --installPath "C:\Program Files\Microsoft Visual Studio\2022\Community" --add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended --passive --norestart

If you prefer a separate Build Tools install:
  winget install --id Microsoft.VisualStudio.2022.BuildTools -e --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive --norestart"

Then rerun this launcher.
"@
        }

        Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "ninja")
        Invoke-NativeCommand -FilePath $pythonPath -Arguments @("-m", "pip", "install", "--no-build-isolation", "./texture_baker", "./uv_unwrapper")
    }
    finally {
        Pop-Location
    }

    return $pythonPath
}

function Start-PowerShellWindow {
    param(
        [string]$Title,
        [string]$WorkingDirectory,
        [string]$Command
    )

    $escapedTitle = $Title.Replace("'", "''")
    $escapedDirectory = $WorkingDirectory.Replace("'", "''")
    $fullCommand = "`$Host.UI.RawUI.WindowTitle = '$escapedTitle'; Set-Location -LiteralPath '$escapedDirectory'; $Command"
    Start-Process powershell -ArgumentList @("-NoExit", "-ExecutionPolicy", "Bypass", "-Command", $fullCommand)
}

function Quote-PowerShellArgument {
    param([string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function Test-Sf3dHealth {
    param(
        [string]$HostName,
        [int]$Port
    )

    try {
        $health = Invoke-RestMethod -Uri "http://${HostName}:${Port}/health" -TimeoutSec 2
        return $health.ok -eq $true
    }
    catch {
        return $false
    }
}

function Wait-Sf3dHealth {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Sf3dHealth -HostName $HostName -Port $Port) {
            return $true
        }

        Start-Sleep -Seconds 1
    }

    return $false
}

function Copy-ShadowOutputToUnity {
    $sourceDir = Join-Path $CapDir "output"
    $meshPath = Join-Path $sourceDir "shadow_mesh.obj"
    $metadataPath = Join-Path $sourceDir "shadow_metadata.json"

    if (-not (Test-Path -LiteralPath $meshPath)) {
        throw "Shadow mesh was not created: $meshPath"
    }

    New-Item -ItemType Directory -Force -Path $UnityWatchDir | Out-Null

    if ((Resolve-Path -LiteralPath $sourceDir).Path -ne (Resolve-Path -LiteralPath $UnityWatchDir).Path) {
        Copy-Item -LiteralPath $meshPath -Destination (Join-Path $UnityWatchDir "shadow_mesh.obj") -Force

        if (Test-Path -LiteralPath $metadataPath) {
            Copy-Item -LiteralPath $metadataPath -Destination (Join-Path $UnityWatchDir "shadow_metadata.json") -Force
        }

        foreach ($previewName in @("shadow_mask.png", "shadow_contour.png", "shadow_mesh_preview.png")) {
            $previewPath = Join-Path $sourceDir $previewName
            if (Test-Path -LiteralPath $previewPath) {
                Copy-Item -LiteralPath $previewPath -Destination (Join-Path $UnityWatchDir $previewName) -Force
            }
        }
    }

    Write-Host "[ok] Unity watch folder updated: $UnityWatchDir"
}

Write-Section "Unity AI Shadow Pipeline - Windows CUDA/Webcam Launcher"
Write-Host "Root          : $Root"
Write-Host "Unity project : $UnityProjectPath"
Write-Host "Capture camera: $CaptureCamera"
Write-Host "Hand camera   : $HandCamera"
Write-Host "Unity UDP     : 127.0.0.1:$UnityUdpPort"
Write-Host "SF3D enabled  : $(-not $SkipSf3d)"
Write-Host "SF3D only     : $Sf3dServerOnly"

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
New-Item -ItemType Directory -Force -Path $UnityWatchDir | Out-Null

if ($Sf3dServerOnly -and $SkipSf3d) {
    throw "-Sf3dServerOnly cannot be used with -SkipSf3d."
}

if (-not $Sf3dServerOnly) {
    Write-Section "Setting up Python environments"
    $capPython = Ensure-Venv -Directory $CapDir -RequirementsPath (Join-Path $CapDir "requirements.txt")
    $handPython = Ensure-Venv -Directory $HandDir -RequirementsPath (Join-Path $HandDir "requirements.txt")
}

if (-not $SkipSf3d) {
    $sf3dAppDir = Resolve-Sf3dAppDirectory -RequestedPath $Sf3dWorkerDir
    $sf3dPython = Ensure-Sf3dVenv -AppDir $sf3dAppDir
    $sf3dHost = "127.0.0.1"
    $sf3dPort = 8000

    Write-Section "Starting SF3D API server"
    if (Test-Sf3dHealth -HostName $sf3dHost -Port $sf3dPort) {
        Write-Host "[ok] SF3D server is already responding at http://${sf3dHost}:${sf3dPort}"
    }
    else {
        Start-PowerShellWindow `
            -Title "SF3D API Server" `
            -WorkingDirectory $sf3dAppDir `
            -Command "& $(Quote-PowerShellArgument $sf3dPython) -m uvicorn app:app --host $sf3dHost --port $sf3dPort"

        Write-Host "[wait] Waiting for SF3D server health at http://${sf3dHost}:${sf3dPort}/health"
        if (-not (Wait-Sf3dHealth -HostName $sf3dHost -Port $sf3dPort)) {
            throw "SF3D server window opened, but http://${sf3dHost}:${sf3dPort}/health did not respond. Check the SF3D API Server window for errors."
        }

        Write-Host "[ok] SF3D server is responding at http://${sf3dHost}:${sf3dPort}"
    }

    if ($Sf3dServerOnly) {
        Write-Host "[ready] SF3D server only mode. Open Unity, press Play, then press Enter after capture/deformation."
        return
    }
}

if ($OpenUnity) {
    Write-Section "Opening Unity"
    if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
        $UnityEditorPath = "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe"
    }

    if (Test-Path -LiteralPath $UnityEditorPath) {
        Start-Process -FilePath $UnityEditorPath -ArgumentList @("-projectPath", $UnityProjectPath)
    }
    else {
        Write-Warning "Unity editor was not found at $UnityEditorPath. Open the project manually in Unity Hub 2022.3."
    }
}

Write-Section "Step 1 - Shadow capture"
Write-Host "Capture window controls:"
Write-Host "  SPACE once: background without object"
Write-Host "  SPACE again: shadow with object"
Write-Host "  ESC: cancel"

$captureArgs = @("--mode", "live")
if ($UseIpCamera) {
    $captureArgs += @("--camera-url", $IpCameraUrl)
}
else {
    $captureArgs += @("--camera", "$CaptureCamera", "--no-camera-fallback")
}
$captureArgs += @(
    "--width", "$CameraWidth",
    "--height", "$CameraHeight",
    "--fps", "$CameraFps",
    "--epsilon", "$ShadowEpsilon",
    "--spacing", "$ShadowSpacing"
)

Push-Location -LiteralPath $CapDir
try {
    $captureScript = Join-Path $CapDir "shadow_capture.py"
    & $capPython $captureScript @captureArgs
}
finally {
    Pop-Location
}

Copy-ShadowOutputToUnity

Write-Section "Step 2 - Hand tracking"
$handArgs = @()
if ($UseIpCamera) {
    $handArgs += @("--camera-url", $IpCameraUrl)
}
else {
    $handArgs += @("--camera", "$HandCamera", "--skip-camera", "$CaptureCamera")
}
$handArgs += @(
    "--width", "$HandCameraWidth",
    "--height", "$HandCameraHeight",
    "--fps", "$HandCameraFps",
    "--retry-forever",
    "--retry-interval", "3",
    "--packet-width", "$CameraWidth",
    "--packet-height", "$CameraHeight",
    "--udp-host", "127.0.0.1",
    "--udp-port", "$UnityUdpPort"
)

$quotedHandArgs = ($handArgs | ForEach-Object { Quote-PowerShellArgument $_ }) -join " "
$handScript = Join-Path $HandDir "main.py"
Start-PowerShellWindow `
    -Title "MediaPipe Hand Tracking" `
    -WorkingDirectory $HandDir `
    -Command "& $(Quote-PowerShellArgument $handPython) $(Quote-PowerShellArgument $handScript) $quotedHandArgs"

Write-Host ""
Write-Host "[ready] Open Unity 2022.3, load Assets/Scenes/Main.unity, then press Play."
Write-Host "[ready] Unity loads: $UnityWatchDir\shadow_mesh.obj"
Write-Host "[ready] Hand tracking sends UDP to 127.0.0.1:$UnityUdpPort"
Write-Host "[ready] In Unity, press Enter after deformation to run ControlNet + SF3D."
