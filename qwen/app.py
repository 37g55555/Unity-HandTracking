import gc
import io
import threading
import time
import warnings

import cv2
import numpy as np
import torch
from fastapi import FastAPI, File, HTTPException, UploadFile
from PIL import Image

from silhouette_labeler import get_labeler, infer_silhouette_label, unload_labeler


warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", message=".*use_fast.*")
warnings.filterwarnings("ignore", message=".*do_sample.*temperature.*")
warnings.filterwarnings("ignore", message=".*weights_only=False.*")

app = FastAPI(
    title="Qwen Keyword API",
    description="Receives Unity shadow PNGs and returns Qwen silhouette keywords.",
)

device = "cuda" if torch.cuda.is_available() else "cpu"
qwen_operation_lock = threading.Lock()


def log(message: str):
    print(message, flush=True)


def elapsed_seconds(started_at: float) -> str:
    return f"{time.perf_counter() - started_at:.1f}s"


def clear_memory():
    gc.collect()
    if torch.cuda.is_available():
        try:
            torch.cuda.synchronize()
        except RuntimeError:
            pass
        torch.cuda.empty_cache()
        try:
            torch.cuda.ipc_collect()
        except RuntimeError:
            pass


def extract_silhouette_mask(image: Image.Image) -> Image.Image:
    image_rgba = image.convert("RGBA").resize(
        (512, 512),
        Image.Resampling.LANCZOS,
    )
    rgba = np.array(image_rgba)
    alpha = rgba[:, :, 3]

    if alpha.max() > alpha.min():
        mask = alpha
    else:
        luminance = cv2.cvtColor(rgba[:, :, :3], cv2.COLOR_RGB2GRAY)
        if luminance.mean() > 127:
            mask = np.where(luminance < 220, 255, 0).astype(np.uint8)
        else:
            mask = np.where(luminance > 35, 255, 0).astype(np.uint8)

    _, mask = cv2.threshold(mask, 8, 255, cv2.THRESH_BINARY)
    if cv2.countNonZero(mask) == 0:
        raise HTTPException(status_code=400, detail="Input PNG has no usable shadow silhouette.")

    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if contours:
        filled_mask = np.zeros_like(mask)
        cv2.drawContours(filled_mask, contours, -1, 255, cv2.FILLED)
        mask = filled_mask

    return Image.fromarray(mask, mode="L")


@app.get("/health")
async def health():
    return {
        "ok": True,
        "device": device,
        "cuda": torch.cuda.is_available(),
        "service": "qwen-keyword",
    }


@app.post("/warmup-labeler")
async def warmup_labeler():
    started_at = time.perf_counter()
    log("[Qwen] warmup requested.")
    with qwen_operation_lock:
        clear_memory()
        get_labeler(device)

    log(f"[Qwen] warmup complete ({elapsed_seconds(started_at)}).")
    return {"ok": True}


@app.post("/classify-silhouette")
async def classify_silhouette(file: UploadFile = File(...)):
    started_at = time.perf_counter()
    log(f"[Qwen] classification queued: {file.filename}")

    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes))
    silhouette_mask = extract_silhouette_mask(image)
    qwen_input = silhouette_mask.convert("RGB")

    with qwen_operation_lock:
        qwen_model, qwen_processor = get_labeler(device)
        log("[Qwen] model ready; running classification.")
        try:
            label = infer_silhouette_label(
                qwen_input,
                device,
                qwen_model,
                qwen_processor,
            )
        finally:
            unload_labeler()
            clear_memory()

    if not label:
        raise HTTPException(status_code=502, detail="Qwen returned an empty silhouette label.")

    log(f"[Qwen] keyword: {label} ({elapsed_seconds(started_at)}).")
    return {"label": label}


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8000)
