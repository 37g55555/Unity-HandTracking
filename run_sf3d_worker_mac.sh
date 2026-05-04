#!/usr/bin/env bash
set -euo pipefail

WORKER_DIR="${SF3D_WORKER_DIR:-$HOME/Downloads/sf3d_worker/sf3d_worker}"
PYTHON_BIN="${SF3D_PYTHON:-$WORKER_DIR/.venv/bin/python}"

if [[ ! -d "$WORKER_DIR" ]]; then
  echo "[error] SF3D worker directory not found: $WORKER_DIR"
  echo "        Set SF3D_WORKER_DIR=/path/to/sf3d_worker/sf3d_worker"
  exit 1
fi

cd "$WORKER_DIR"

if [[ ! -x "$PYTHON_BIN" ]]; then
  echo "[setup] Creating SF3D virtual environment..."
  python3 -m venv .venv
  PYTHON_BIN="$WORKER_DIR/.venv/bin/python"
fi

echo "[setup] Installing API requirements..."
"$PYTHON_BIN" -m pip install --upgrade pip
"$PYTHON_BIN" -m pip install -r requirements_api.txt

echo "[run] Starting SF3D API server at http://127.0.0.1:8000"
echo "[run] Unity will send deformed_shadow.png to /generate-texture and /generate-3d"
PYTORCH_ENABLE_MPS_FALLBACK=1 "$PYTHON_BIN" app.py
