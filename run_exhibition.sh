#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CAP_DIR="$ROOT_DIR/CAP_II"
HAND_DIR="$ROOT_DIR/3d Hand Tracking"
LOG_DIR="$ROOT_DIR/logs"

detect_windows_host_ip() {
  if command -v ip >/dev/null 2>&1; then
    ip route | awk '/default/ {print $3; exit}'
  fi
}

USE_IP_CAMERA="${USE_IP_CAMERA:-1}"
IP_CAMERA_URL="${IP_CAMERA_URL:-http://192.168.0.12:8081/video}"
CAPTURE_CAMERA="${CAPTURE_CAMERA:-0}"
HAND_CAMERA="${HAND_CAMERA:-1}"
UNITY_UDP_PORT="${UNITY_UDP_PORT:-5052}"
UNITY_UDP_HOST="${UNITY_UDP_HOST:-$(detect_windows_host_ip)}"
UNITY_UDP_HOST="${UNITY_UDP_HOST:-127.0.0.1}"

if [[ -z "${UNITY_PROJECT_PATH:-}" ]]; then
  if [[ -d "/mnt/c/Users/$USER/Desktop/My project" ]]; then
    UNITY_PROJECT_PATH="/mnt/c/Users/$USER/Desktop/My project"
  else
    UNITY_PROJECT_PATH="$(find /mnt/c/Users -maxdepth 3 -type d -path '*/Desktop/My project' 2>/dev/null | head -n 1 || true)"
  fi
fi

mkdir -p "$LOG_DIR"

echo "============================================================"
echo "Unity AI Shadow Pipeline - WSL Exhibition Launcher"
echo "============================================================"
echo "Camera mode   : $([[ "$USE_IP_CAMERA" == "1" ]] && echo "IP camera" || echo "USB webcams")"
echo "IP camera URL : $IP_CAMERA_URL"
echo "USB cameras   : capture=$CAPTURE_CAMERA, hand=$HAND_CAMERA"
echo "Unity UDP host: $UNITY_UDP_HOST:$UNITY_UDP_PORT"
echo "Unity project : ${UNITY_PROJECT_PATH:-not found}"
echo

ensure_venv() {
  local dir="$1"
  local requirements="$2"
  if [[ ! -x "$dir/.venv/bin/python" ]]; then
    echo "[setup] Creating venv: $dir/.venv"
    python3 -m venv "$dir/.venv"
  fi

  echo "[setup] Installing requirements: $requirements"
  "$dir/.venv/bin/python" -m pip install --upgrade pip >/dev/null
  "$dir/.venv/bin/python" -m pip install -r "$requirements"
}

sync_unity_project() {
  if [[ -z "${UNITY_PROJECT_PATH:-}" || ! -d "$UNITY_PROJECT_PATH/Assets" ]]; then
    echo "[warn] Unity project path not found. Set UNITY_PROJECT_PATH manually if sync is needed."
    return
  fi

  echo "[sync] Copying Unity runtime scripts to Windows project..."
  mkdir -p "$UNITY_PROJECT_PATH/Assets/Scripts"
  if command -v rsync >/dev/null 2>&1; then
    rsync -a "$ROOT_DIR/UnityProject/Assets/Scripts/" "$UNITY_PROJECT_PATH/Assets/Scripts/"
  else
    cp -R "$ROOT_DIR/UnityProject/Assets/Scripts/." "$UNITY_PROJECT_PATH/Assets/Scripts/"
  fi
}

sync_shadow_output_to_unity() {
  if [[ -z "${UNITY_PROJECT_PATH:-}" || ! -d "$UNITY_PROJECT_PATH" ]]; then
    echo "[warn] Unity project path not found. Shadow OBJ was not mirrored."
    return
  fi

  local source_dir="$CAP_DIR/output"
  local target_dir="$UNITY_PROJECT_PATH/sf3d_io/live_shadow"
  if [[ ! -f "$source_dir/shadow_mesh.obj" ]]; then
    echo "[warn] Shadow mesh not found: $source_dir/shadow_mesh.obj"
    return
  fi

  echo "[sync] Copying generated shadow mesh to Unity watch folder..."
  mkdir -p "$target_dir"
  cp "$source_dir/shadow_mesh.obj" "$target_dir/shadow_mesh.obj"

  if [[ -f "$source_dir/shadow_metadata.json" ]]; then
    cp "$source_dir/shadow_metadata.json" "$target_dir/shadow_metadata.json"
  fi

  if [[ -f "$source_dir/deformed_shadow.png" ]]; then
    cp "$source_dir/deformed_shadow.png" "$target_dir/deformed_shadow.png"
  fi

  echo "[ok] Unity watch folder updated: $target_dir"
}

build_capture_camera_args() {
  if [[ "$USE_IP_CAMERA" == "1" ]]; then
    CAPTURE_CAMERA_ARGS=("--camera-url" "$IP_CAMERA_URL")
  else
    CAPTURE_CAMERA_ARGS=("--camera" "$CAPTURE_CAMERA" "--no-camera-fallback")
  fi
}

build_hand_camera_args() {
  if [[ "$USE_IP_CAMERA" == "1" ]]; then
    HAND_CAMERA_ARGS=("--camera-url" "$IP_CAMERA_URL")
  else
    HAND_CAMERA_ARGS=("--camera" "$HAND_CAMERA" "--no-camera-fallback")
  fi
}

stop_existing_python() {
  pkill -f "$CAP_DIR/shadow_capture.py" 2>/dev/null || true
  pkill -f "$HAND_DIR/main.py" 2>/dev/null || true
}

ensure_venv "$CAP_DIR" "$CAP_DIR/requirements.txt"
ensure_venv "$HAND_DIR" "$HAND_DIR/requirements.txt"
sync_unity_project
stop_existing_python

echo
echo "[step 1] Shadow capture will open first."
echo "         Space: capture background / shadow"
echo "         ESC  : quit capture"
echo
build_capture_camera_args
"$CAP_DIR/.venv/bin/python" "$CAP_DIR/shadow_capture.py" \
  --mode live \
  "${CAPTURE_CAMERA_ARGS[@]}"

sync_shadow_output_to_unity

echo
echo "[step 2] Starting MediaPipe hand tracking in background..."
build_hand_camera_args
nohup "$HAND_DIR/.venv/bin/python" "$HAND_DIR/main.py" \
  "${HAND_CAMERA_ARGS[@]}" \
  --udp-host "$UNITY_UDP_HOST" \
  --udp-port "$UNITY_UDP_PORT" \
  > "$LOG_DIR/hand_tracking.log" 2>&1 &
HAND_PID=$!

echo "[ok] Hand tracking PID: $HAND_PID"
echo "[ok] Log: $LOG_DIR/hand_tracking.log"
echo
echo "Now open Unity on Windows and press Play."
echo "Unity should load sf3d_io/live_shadow/shadow_mesh.obj and receive UDP on port $UNITY_UDP_PORT."
