import gc
import io
import os
import re
import threading
import time
import warnings
from contextlib import nullcontext
from contextlib import redirect_stdout

import cv2
import numpy as np
import torch
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import FileResponse, StreamingResponse
from PIL import Image, ImageEnhance
from starlette.background import BackgroundTask
from tqdm.auto import tqdm

from sf3d.system import SF3D
from sf3d.utils import get_device, resize_foreground
from silhouette_labeler import get_labeler, infer_silhouette_interpretation, unload_labeler


warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", message=".*use_fast.*")
warnings.filterwarnings("ignore", message=".*do_sample.*temperature.*")
warnings.filterwarnings("ignore", message=".*weights_only=False.*")

app = FastAPI(
    title="Unity API",
    description="Receives Unity shadow PNGs and returns GLB models.",
)

device = get_device()
if not torch.cuda.is_available():
    device = "cpu"

model = None
texture_pipe = None
qwen_operation_lock = threading.Lock()
texture_operation_lock = threading.Lock()
texture_resolution = 512
sf3d_bake_resolution = 1024
sf3d_remesh_mode = "none"
sf3d_vertex_count = -1
sf3d_foreground_ratio = 0.85
repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
temp_output_dir = os.path.join(repo_root, "output", "api_temp")
texture_model_id = "stable-diffusion-v1-5/stable-diffusion-v1-5"
texture_controlnet_model_id = "lllyasviel/sd-controlnet-canny"
texture_inference_steps = 28
texture_guidance_scale = 7.5
texture_controlnet_conditioning_scale = 1.25


def log(message: str):
    print(message, flush=True)


def elapsed_seconds(started_at: float) -> str:
    return f"{time.perf_counter() - started_at:.1f}s"


def acquire_texture_lock(label: str):
    started_at = time.perf_counter()
    if not texture_operation_lock.acquire(blocking=False):
        log(f"[ControlNet] waiting for texture lock: {label}")
        texture_operation_lock.acquire()
        log(f"[ControlNet] texture lock acquired after {elapsed_seconds(started_at)}: {label}")
    else:
        log(f"[ControlNet] texture lock acquired: {label}")


def progress(label: str, total: int):
    return tqdm(desc=label, total=total, ncols=90, leave=True)


def cleanup_temp_file(filepath: str):
    try:
        if os.path.isfile(filepath):
            os.remove(filepath)
        os.rmdir(temp_output_dir)
    except OSError:
        pass


def extract_silhouette_mask(image: Image.Image) -> Image.Image:
    image_rgba = image.convert("RGBA").resize(
        (texture_resolution, texture_resolution),
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


def apply_silhouette_alpha(texture: Image.Image, mask_image: Image.Image) -> Image.Image:
    texture_rgba = texture.convert("RGBA").resize(
        (texture_resolution, texture_resolution),
        Image.Resampling.LANCZOS,
    )
    alpha = mask_image.resize(
        (texture_resolution, texture_resolution),
        Image.Resampling.LANCZOS,
    )
    rgba = np.array(texture_rgba)
    alpha_array = np.array(alpha)
    rgba[:, :, 3] = alpha_array
    rgba[alpha_array == 0, :3] = 0
    return Image.fromarray(rgba, mode="RGBA")


def build_control_image(mask_image: Image.Image) -> Image.Image:
    mask = np.array(
        mask_image.resize((texture_resolution, texture_resolution), Image.Resampling.NEAREST)
    )
    _, mask = cv2.threshold(mask, 8, 255, cv2.THRESH_BINARY)
    edges = cv2.Canny(mask, 64, 160)
    edges = cv2.dilate(edges, np.ones((3, 3), np.uint8), iterations=1)
    edge_rgb = np.repeat(edges[:, :, None], 3, axis=2)
    return Image.fromarray(edge_rgb, mode="RGB")


def enhance_texture_color(texture: Image.Image) -> Image.Image:
    texture_rgb = texture.convert("RGB")
    texture_rgb = ImageEnhance.Color(texture_rgb).enhance(1.18)
    texture_rgb = ImageEnhance.Brightness(texture_rgb).enhance(1.0)
    texture_rgb = ImageEnhance.Contrast(texture_rgb).enhance(1.1)
    return texture_rgb


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


def unload_texture_pipeline():
    global texture_pipe
    if texture_pipe is not None:
        del texture_pipe
        texture_pipe = None
    clear_memory()


def clean_prompt_fragment(value: str, max_words: int = 18) -> str:
    value = (value or "").strip().lower()
    value = re.sub(r"[^a-z0-9 ,\-]", " ", value)
    value = re.sub(r"\s+", " ", value).strip(" ,")
    return " ".join(value.split()[:max_words])


def build_texture_prompt(label: str, visual_hint: str) -> str:
    clean_label = clean_prompt_fragment(label, max_words=4)
    if not clean_label:
        raise HTTPException(status_code=400, detail="Texture generation requires a non-empty silhouette label.")

    clean_visual_hint = clean_prompt_fragment(visual_hint, max_words=12)
    if not clean_visual_hint:
        raise HTTPException(status_code=400, detail="Texture generation requires a non-empty visual hint.")

    return (
        "realistic full-color 3D object, "
        f"{clean_label}, {clean_visual_hint}, "
        "one coherent volume filling the entire silhouette edge to edge, "
        "sharp albedo details, crisp silhouette boundary, clean object-background separation, "
        "neutral lighting, no background"
    )


def build_texture_negative_prompt() -> str:
    return (
        "background, padding, empty margins, centered separate object, flat poster, "
        "text, watermark, grayscale, blurry, abstract pattern, geometric panels, "
        "collage, neon, hard stripes, low contrast edge, blended background"
    )


def unload_sf3d_model():
    global model
    if model is not None:
        log("[SF3D] unloading model from CUDA memory...")
        del model
        model = None
    clear_memory()


def get_sf3d_model():
    global model
    if model is None:
        unload_texture_pipeline()
        started_at = time.perf_counter()
        with progress("[SF3D] load", 3) as bar:
            try:
                model = SF3D.from_pretrained(
                    "stabilityai/stable-fast-3d",
                    config_name="config.yaml",
                    weight_name="model.safetensors",
                )
                bar.update(1)
            except Exception as exc:
                message = str(exc)
                if "Cannot access gated repo" in message or "401 Client Error" in message:
                    raise HTTPException(
                        status_code=401,
                        detail=(
                            "Cannot access stabilityai/stable-fast-3d. "
                            "Log in to Hugging Face in this SF3D environment and accept "
                            "the model access terms on https://huggingface.co/stabilityai/stable-fast-3d."
                        ),
                    ) from exc
                raise
            model.to(device)
            bar.update(1)
            model.eval()
            bar.update(1)
        log(f"[SF3D] model ready ({elapsed_seconds(started_at)}).")
    return model


def get_texture_pipeline():
    global texture_pipe
    if texture_pipe is None:
        unload_sf3d_model()
        from diffusers import (
            ControlNetModel,
            StableDiffusionControlNetPipeline,
            UniPCMultistepScheduler,
        )

        dtype = torch.float16 if device == "cuda" else torch.float32
        log("[ControlNet] loading texture pipeline...")
        controlnet = ControlNetModel.from_pretrained(
            texture_controlnet_model_id,
            torch_dtype=dtype,
        )
        texture_pipe = StableDiffusionControlNetPipeline.from_pretrained(
            texture_model_id,
            controlnet=controlnet,
            torch_dtype=dtype,
            safety_checker=None,
            requires_safety_checker=False,
        )
        texture_pipe.scheduler = UniPCMultistepScheduler.from_config(texture_pipe.scheduler.config)
        texture_pipe.to(device)
        log("[ControlNet] texture pipeline loaded.")
    return texture_pipe


def prepare_labeler_memory():
    with texture_operation_lock:
        unload_texture_pipeline()
    unload_sf3d_model()


@app.get("/health")
async def health():
    return {
        "ok": True,
        "device": device,
        "cuda": torch.cuda.is_available(),
        "sf3d_loaded": model is not None,
        "texture_pipeline": "sd15_controlnet_canny_txt2img",
        "texture_loaded": texture_pipe is not None,
        "texture_resolution": texture_resolution,
        "texture_steps": texture_inference_steps,
        "texture_guidance_scale": texture_guidance_scale,
        "texture_model": texture_model_id,
        "texture_controlnet_model": texture_controlnet_model_id,
        "texture_controlnet_conditioning_scale": texture_controlnet_conditioning_scale,
    }


@app.post("/warmup-labeler")
async def warmup_labeler():
    started_at = time.perf_counter()
    log("[Qwen] warmup requested.")
    with qwen_operation_lock:
        prepare_labeler_memory()
        get_labeler(device)

    log(f"[Qwen] warmup complete ({elapsed_seconds(started_at)}).")
    return {"ok": True}


@app.post("/warmup-texture")
async def warmup_texture():
    started_at = time.perf_counter()
    log("[ControlNet] warmup requested.")
    acquire_texture_lock("warmup")
    try:
        get_texture_pipeline()
    finally:
        texture_operation_lock.release()
    log(f"[ControlNet] warmup complete ({elapsed_seconds(started_at)}).")
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
        prepare_labeler_memory()
        qwen_model, qwen_processor = get_labeler(device)
        log("[Qwen] model ready; running classification.")
        try:
            interpretation = infer_silhouette_interpretation(
                qwen_input,
                device,
                qwen_model,
                qwen_processor,
            )
        finally:
            unload_labeler()
            clear_memory()

    label = interpretation.get("label", "")
    visual_hint = interpretation.get("visual_hint", "")
    if not label:
        raise HTTPException(status_code=502, detail="Qwen returned an empty silhouette label.")
    if not visual_hint:
        raise HTTPException(status_code=502, detail="Qwen returned an empty visual hint.")

    log(f"[Qwen] label: {label}, visual_hint: {visual_hint} ({elapsed_seconds(started_at)}).")
    return {"label": label, "visual_hint": visual_hint}


@app.post("/generate-texture")
async def generate_texture(
    file: UploadFile = File(...),
    label: str = Form(...),
    visual_hint: str = Form(...),
):
    started_at = time.perf_counter()
    log(f"[ControlNet] texture requested: {file.filename}, label={label}, visual_hint={visual_hint}")

    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes))
    silhouette_mask = extract_silhouette_mask(image)
    control_image = build_control_image(silhouette_mask)

    prompt = build_texture_prompt(label, visual_hint)
    log(f"[ControlNet] prompt: {prompt}")
    negative_prompt = build_texture_negative_prompt()

    acquire_texture_lock("generate-texture")
    try:
        pipe = get_texture_pipeline()
        inference_started_at = time.perf_counter()
        log(
            "[ControlNet] inference started "
            f"({texture_inference_steps} steps, {texture_resolution}x{texture_resolution}, "
            f"guidance={texture_guidance_scale}, control={texture_controlnet_conditioning_scale})."
        )
        output = pipe(
            prompt,
            image=control_image,
            negative_prompt=negative_prompt,
            height=texture_resolution,
            width=texture_resolution,
            num_inference_steps=texture_inference_steps,
            guidance_scale=texture_guidance_scale,
            controlnet_conditioning_scale=texture_controlnet_conditioning_scale,
        ).images[0]
        log(f"[ControlNet] inference finished ({elapsed_seconds(inference_started_at)}).")
    finally:
        unload_texture_pipeline()
        texture_operation_lock.release()

    output = enhance_texture_color(output)
    output = apply_silhouette_alpha(output, silhouette_mask)

    img_byte_arr = io.BytesIO()
    output.save(img_byte_arr, format="PNG")
    img_byte_arr.seek(0)

    log(f"[ControlNet] texture complete ({elapsed_seconds(started_at)}).")
    return StreamingResponse(img_byte_arr, media_type="image/png")


@app.post("/generate-3d")
async def generate_3d(file: UploadFile = File(...)):
    started_at = time.perf_counter()
    log(f"[SF3D] model requested: {file.filename}")

    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes)).convert("RGBA")

    sf3d_model = get_sf3d_model()
    try:
        with progress("[SF3D] run", 3) as bar:
            image = resize_foreground(image, sf3d_foreground_ratio)
            bar.update(1)

            autocast_context = (
                torch.autocast(device_type=device, dtype=torch.bfloat16)
                if device == "cuda"
                else nullcontext()
            )
            with torch.no_grad():
                with autocast_context:
                    with redirect_stdout(io.StringIO()):
                        mesh, _ = sf3d_model.run_image(
                            [image],
                            bake_resolution=sf3d_bake_resolution,
                            remesh=sf3d_remesh_mode,
                            vertex_count=sf3d_vertex_count,
                        )
            bar.update(1)

            os.makedirs(temp_output_dir, exist_ok=True)
            out_mesh_path = os.path.join(temp_output_dir, f"{time.time_ns()}.glb")
            mesh.export(out_mesh_path, include_normals=True)
            bar.update(1)
        log(f"[SF3D] generated: {out_mesh_path} ({elapsed_seconds(started_at)}).")
    finally:
        unload_sf3d_model()

    return FileResponse(
        out_mesh_path,
        media_type="model/gltf-binary",
        filename="model.glb",
        background=BackgroundTask(cleanup_temp_file, out_mesh_path),
    )


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8000)
