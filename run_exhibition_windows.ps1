param(
    [int]$CaptureCamera = 0,
    [int]$HandCamera = 1,
    [int]$UnityUdpPort = 5052,
    [string]$UnityProjectPath = "$PSScriptRoot\UnityProject",
    [string]$Sf3dWorkerDir = "",
    [string]$TorchIndexUrl = "https://download.pytorch.org/whl/cu121",
    [switch]$UseIpCamera,
    [string]$IpCameraUrl = "http://192.168.0.12:8081/video",
    [switch]$UseRembgGpu,
    [switch]$InstallXformers,
    [switch]$SkipDependencyInstall,
    [switch]$SkipSf3d,
    [switch]$OpenUnity,
    [string]$UnityEditorPath = "",
    [float]$ShadowEpsilon = 0.002,
    [float]$ShadowSpacing = 8,
    [float]$ShadowBoundarySpacing = 8
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$CapDir = Join-Path $Root "CAP_II"
$HandDir = Join-Path $Root "3d Hand Tracking"
$LogDir = Join-Path $Root "logs"
$LiveShadowDir = Join-Path $UnityProjectPath "sf3d_io\live_shadow"

function Write-Section {
    param([string]$Text)
    Write-Host ""
    Write-Host "============================================================"
    Write-Host $Text
    Write-Host "============================================================"
}

function New-PythonVenv {
    param([string]$VenvDir)

    if (Get-Command py -ErrorAction SilentlyContinue) {
        & py -3.11 -m venv $VenvDir
        return
    }

    if (Get-Command python -ErrorAction SilentlyContinue) {
        & python -m venv $VenvDir
        return
    }

    throw "Python was not found. Install Python 3.11 and enable 'Add python.exe to PATH'."
}

function Ensure-Venv {
    param(
        [string]$Directory,
        [string]$RequirementsPath
    )

    $venvDir = Join-Path $Directory ".venv"
    $pythonPath = Join-Path $venvDir "Scripts\python.exe"

    if (-not (Test-Path -LiteralPath $pythonPath)) {
        Write-Host "[setup] Creating venv: $venvDir"
        New-PythonVenv -VenvDir $venvDir
    }

    if (-not $SkipDependencyInstall) {
        Write-Host "[setup] Installing requirements: $RequirementsPath"
        & $pythonPath -m pip install --upgrade pip
        & $pythonPath -m pip install -r $RequirementsPath
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
    $pythonPath = Join-Path $venvDir "Scripts\python.exe"

    if (-not (Test-Path -LiteralPath $pythonPath)) {
        Write-Host "[setup] Creating SF3D venv: $venvDir"
        New-PythonVenv -VenvDir $venvDir
    }

    if ($SkipDependencyInstall) {
        return $pythonPath
    }

    Push-Location -LiteralPath $AppDir
    try {
        Write-Host "[setup] Installing SF3D CUDA torch wheels from $TorchIndexUrl"
        & $pythonPath -m pip install --upgrade pip
        & $pythonPath -m pip install torch torchvision torchaudio --index-url $TorchIndexUrl

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
                    $_ -notmatch "^\s*rembg(\[.*\])?(\s|=|>|<|;|$)"
                } |
                Set-Content -LiteralPath $tempReq -Encoding UTF8

            Write-Host "[setup] Installing SF3D/API requirements, excluding torch/xformers/rembg duplicates"
            & $pythonPath -m pip install -r $tempReq
        }

        if ($UseRembgGpu) {
            & $pythonPath -m pip install "rembg[gpu]"
        }
        else {
            & $pythonPath -m pip install "rembg[cpu]"
        }

        if ($InstallXformers) {
            & $pythonPath -m pip install xformers
        }
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

function Copy-ShadowOutputToUnity {
    $sourceDir = Join-Path $CapDir "output"
    $meshPath = Join-Path $sourceDir "shadow_mesh.obj"
    $metadataPath = Join-Path $sourceDir "shadow_metadata.json"

    if (-not (Test-Path -LiteralPath $meshPath)) {
        throw "Shadow mesh was not created: $meshPath"
    }

    New-Item -ItemType Directory -Force -Path $LiveShadowDir | Out-Null
    Copy-Item -LiteralPath $meshPath -Destination (Join-Path $LiveShadowDir "shadow_mesh.obj") -Force

    if (Test-Path -LiteralPath $metadataPath) {
        Copy-Item -LiteralPath $metadataPath -Destination (Join-Path $LiveShadowDir "shadow_metadata.json") -Force
    }

    foreach ($previewName in @("shadow_mask.png", "shadow_contour.png", "shadow_mesh_preview.png")) {
        $previewPath = Join-Path $sourceDir $previewName
        if (Test-Path -LiteralPath $previewPath) {
            Copy-Item -LiteralPath $previewPath -Destination (Join-Path $LiveShadowDir $previewName) -Force
        }
    }

    Write-Host "[ok] Unity watch folder updated: $LiveShadowDir"
}

Write-Section "Unity AI Shadow Pipeline - Windows CUDA/Webcam Launcher"
Write-Host "Root          : $Root"
Write-Host "Unity project : $UnityProjectPath"
Write-Host "Capture camera: $CaptureCamera"
Write-Host "Hand camera   : $HandCamera"
Write-Host "Unity UDP     : 127.0.0.1:$UnityUdpPort"
Write-Host "SF3D enabled  : $(-not $SkipSf3d)"

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
New-Item -ItemType Directory -Force -Path $LiveShadowDir | Out-Null

Write-Section "Setting up Python environments"
$capPython = Ensure-Venv -Directory $CapDir -RequirementsPath (Join-Path $CapDir "requirements.txt")
$handPython = Ensure-Venv -Directory $HandDir -RequirementsPath (Join-Path $HandDir "requirements.txt")

if (-not $SkipSf3d) {
    $sf3dAppDir = Resolve-Sf3dAppDirectory -RequestedPath $Sf3dWorkerDir
    $sf3dPython = Ensure-Sf3dVenv -AppDir $sf3dAppDir

    Write-Section "Starting SF3D API server"
    Start-PowerShellWindow `
        -Title "SF3D API Server" `
        -WorkingDirectory $sf3dAppDir `
        -Command "& $(Quote-PowerShellArgument $sf3dPython) app.py"
    Write-Host "[ok] SF3D server window opened. Unity will call http://127.0.0.1:8000"
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
$captureArgs += @("--epsilon", "$ShadowEpsilon", "--spacing", "$ShadowSpacing", "--boundary-spacing", "$ShadowBoundarySpacing")

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
    $handArgs += @("--camera", "$HandCamera", "--no-camera-fallback")
}
$handArgs += @("--udp-host", "127.0.0.1", "--udp-port", "$UnityUdpPort")

$quotedHandArgs = ($handArgs | ForEach-Object { Quote-PowerShellArgument $_ }) -join " "
$handScript = Join-Path $HandDir "main.py"
Start-PowerShellWindow `
    -Title "MediaPipe Hand Tracking" `
    -WorkingDirectory $HandDir `
    -Command "& $(Quote-PowerShellArgument $handPython) $(Quote-PowerShellArgument $handScript) $quotedHandArgs"

Write-Host ""
Write-Host "[ready] Open Unity 2022.3, load Assets/Scenes/ShadowPrototype.unity, then press Play."
Write-Host "[ready] Unity loads: $LiveShadowDir\shadow_mesh.obj"
Write-Host "[ready] Hand tracking sends UDP to 127.0.0.1:$UnityUdpPort"
Write-Host "[ready] In Unity, press Enter or S after deformation to save PNG and send it to SF3D."
