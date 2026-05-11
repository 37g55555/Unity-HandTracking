@echo off
setlocal

set "WORKER_DIR=%~dp0"
set "ENV_ROOT=%WORKER_DIR%.venv"
set "PYTHON=%ENV_ROOT%\python.exe"
set "VS_DEV_CMD=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"

if not exist "%PYTHON%" (
    echo Python environment was not found: "%PYTHON%"
    exit /b 1
)

if not exist "%VS_DEV_CMD%" (
    echo Visual Studio developer command prompt was not found: "%VS_DEV_CMD%"
    exit /b 1
)

call "%VS_DEV_CMD%" -arch=x64
if errorlevel 1 exit /b %errorlevel%

set "CUDA_HOME=%ENV_ROOT%"
set "CUDA_PATH=%ENV_ROOT%"
set "PATH=%ENV_ROOT%\bin;%ENV_ROOT%\Scripts;%PATH%"
set "TORCH_CUDA_ARCH_LIST=8.9"
set "USE_CUDA=1"
set "DISTUTILS_USE_SDK=1"

where cl
if errorlevel 1 exit /b %errorlevel%

where nvcc
if errorlevel 1 exit /b %errorlevel%

where ninja
if errorlevel 1 exit /b %errorlevel%

"%PYTHON%" -m pip install --force-reinstall --no-build-isolation "%WORKER_DIR%texture_baker"
exit /b %errorlevel%
