import gc
import io
import os
import re
import time
import uuid
import warnings
from contextlib import nullcontext
from contextlib import redirect_stdout

import cv2
import numpy as np
import torch
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import FileResponse, StreamingResponse
from PIL import Image, ImageEnhance
from tqdm.auto import tqdm

from sf3d.system import SF3D
from sf3d.utils import get_device, resize_foreground
from silhouette_labeler import get_labeler, infer_silhouette_label, unload_labeler


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
cn_pipe = None
texture_resolution = int(os.getenv("SF3D_TEXTURE_RESOLUTION", "512"))
sf3d_depth_axis = os.getenv("SF3D_DEPTH_AXIS", "z").lower()
sf3d_min_depth_ratio = float(os.getenv("SF3D_MIN_DEPTH_RATIO", "0.70"))
sf3d_max_depth_scale = float(os.getenv("SF3D_MAX_DEPTH_SCALE", "6.0"))

os.makedirs("temp_outputs", exist_ok=True)


def log(message: str):
    print(message, flush=True)


def elapsed_seconds(started_at: float) -> str:
    return f"{time.perf_counter() - started_at:.1f}s"


def progress(label: str, total: int):
    return tqdm(desc=label, total=total, ncols=90, leave=True)


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

    return Image.fromarray(mask, mode="L")


def build_control_image(mask_image: Image.Image) -> Image.Image:
    mask = np.array(mask_image)
    edges = cv2.Canny(mask, 64, 160)
    edges = cv2.dilate(edges, np.ones((3, 3), np.uint8), iterations=1)
    edge_rgb = np.repeat(edges[:, :, None], 3, axis=2)
    return Image.fromarray(edge_rgb, mode="RGB")


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


def enhance_texture_color(texture: Image.Image) -> Image.Image:
    texture_rgb = texture.convert("RGB")
    texture_rgb = ImageEnhance.Color(texture_rgb).enhance(0.92)
    texture_rgb = ImageEnhance.Brightness(texture_rgb).enhance(1.02)
    texture_rgb = ImageEnhance.Contrast(texture_rgb).enhance(1.02)
    return texture_rgb


def thicken_mesh_depth(mesh):
    if mesh is None or len(mesh.vertices) == 0:
        return mesh

    extents = np.asarray(mesh.extents, dtype=np.float64)
    if extents.size != 3 or np.any(extents <= 1e-6):
        return mesh

    axis_map = {"x": 0, "y": 1, "z": 2}
    if sf3d_depth_axis == "auto":
        depth_axis = int(np.argmin(extents))
    else:
        depth_axis = axis_map.get(sf3d_depth_axis, 2)

    planar_axes = [axis for axis in range(3) if axis != depth_axis]
    target_depth = float(np.max(extents[planar_axes]) * sf3d_min_depth_ratio)
    current_depth = float(extents[depth_axis])
    depth_scale = target_depth / current_depth

    if depth_scale <= 1.001:
        return mesh

    depth_scale = min(depth_scale, sf3d_max_depth_scale)
    vertices = np.asarray(mesh.vertices, dtype=np.float64).copy()
    center = mesh.bounds.mean(axis=0)
    vertices[:, depth_axis] = center[depth_axis] + (
        (vertices[:, depth_axis] - center[depth_axis]) * depth_scale
    )
    mesh.vertices = vertices
    try:
        mesh.fix_normals()
    except Exception:
        pass

    return mesh


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
    global cn_pipe
    if cn_pipe is not None:
        del cn_pipe
        cn_pipe = None
    clear_memory()


def clean_prompt_fragment(value: str, fallback: str, max_words: int = 18) -> str:
    value = (value or "").strip().lower()
    value = re.sub(r"[^a-z0-9 ,\-]", " ", value)
    value = re.sub(r"\s+", " ", value).strip(" ,")
    if not value:
        return fallback

    return " ".join(value.split()[:max_words])


def get_label_material_hint(label: str) -> str:
    if "glove" in label:
        return (
            "soft glove material, matte leather or cotton fabric, smooth padded surface, "
            "subtle seams, no fingerprint lines"
        )

    return "realistic surface material, smooth coherent texture, subtle natural details"


def build_texture_prompt(label: str, category: str = "", texture_hint: str = "") -> str:
    clean_label = clean_prompt_fragment(label, "silhouette object", max_words=4)
    material_hint = get_label_material_hint(clean_label)
    return (
        f"realistic 3d form with subtle {clean_label}-inspired features, "
        "forcibly warped and squeezed to exactly fill the silhouette, "
        f"{material_hint}, volumetric object, natural colors, neutral white lighting, "
        "transparent background, no margins"
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
    global cn_pipe
    if cn_pipe is None:
        unload_sf3d_model()
        from diffusers import (
            ControlNetModel,
            StableDiffusionControlNetPipeline,
            UniPCMultistepScheduler,
        )

        dtype = torch.float16 if device == "cuda" else torch.float32
        controlnet = ControlNetModel.from_pretrained(
            "lllyasviel/sd-controlnet-canny",
            torch_dtype=dtype,
        )
        cn_pipe = StableDiffusionControlNetPipeline.from_pretrained(
            "stable-diffusion-v1-5/stable-diffusion-v1-5",
            controlnet=controlnet,
            torch_dtype=dtype,
            safety_checker=None,
            requires_safety_checker=False,
        )
        cn_pipe.scheduler = UniPCMultistepScheduler.from_config(cn_pipe.scheduler.config)
        cn_pipe.to(device)
    return cn_pipe


@app.get("/health")
async def health():
    return {
        "ok": True,
        "device": device,
        "cuda": torch.cuda.is_available(),
        "sf3d_loaded": model is not None,
        "texture_loaded": cn_pipe is not None,
    }


@app.post("/warmup-labeler")
async def warmup_labeler():
    started_at = time.perf_counter()
    log("[Qwen] warmup requested.")
    get_labeler(device)
    log(f"[Qwen] warmup complete ({elapsed_seconds(started_at)}).")
    return {"ok": True}


@app.post("/warmup-texture")
async def warmup_texture():
    started_at = time.perf_counter()
    log("[ControlNet] warmup requested.")
    get_texture_pipeline()
    log(f"[ControlNet] warmup complete ({elapsed_seconds(started_at)}).")
    return {"ok": True}


@app.post("/classify-silhouette")
async def classify_silhouette(file: UploadFile = File(...)):
    started_at = time.perf_counter()
    log(f"[Qwen] classification requested: {file.filename}")

    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes))
    silhouette_mask = extract_silhouette_mask(image)

    qwen_input = silhouette_mask.convert("RGB")
    try:
        label = infer_silhouette_label(qwen_input, device)
    finally:
        unload_labeler()
        clear_memory()

    log(f"[Qwen] label: {label} ({elapsed_seconds(started_at)}).")
    return {"label": label}


@app.post("/generate-texture")
async def generate_texture(
    file: UploadFile = File(...),
    label: str = Form("object"),
    category: str = Form("abstract"),
    texture_hint: str = Form(""),
):
    started_at = time.perf_counter()
    log(
        "[ControlNet] texture requested: "
        f"{file.filename}, label={label}, category={category}, texture_hint={texture_hint}"
    )

    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes))
    silhouette_mask = extract_silhouette_mask(image)
    control_image = build_control_image(silhouette_mask)

    prompt = build_texture_prompt(label, category, texture_hint)
    log(f"[ControlNet] prompt: {prompt}")
    negative_prompt = (
        "text, watermark, fingers, skin, flat shadow, background, empty margins, "
        "extra objects, duplicate object, unwarped proportions, cropped object, "
        "outside silhouette, wireframe, contour lines, grid, fingerprint, moire, "
        "normal map, neon, colored rim light, oversaturated, green glow"
    )

    pipe = get_texture_pipeline()
    output = pipe(
        prompt,
        image=control_image,
        negative_prompt=negative_prompt,
        height=texture_resolution,
        width=texture_resolution,
        num_inference_steps=28,
        guidance_scale=7.5,
        controlnet_conditioning_scale=1.25,
    ).images[0]
    output = enhance_texture_color(output)
    output = apply_silhouette_alpha(output, silhouette_mask)

    img_byte_arr = io.BytesIO()
    output.save(img_byte_arr, format="PNG")
    img_byte_arr.seek(0)

    unload_texture_pipeline()
    log(f"[ControlNet] texture complete ({elapsed_seconds(started_at)}).")
    return StreamingResponse(img_byte_arr, media_type="image/png")


@app.post("/generate-3d")
async def generate_3d(file: UploadFile = File(...)):
    started_at = time.perf_counter()
    log(f"[SF3D] model requested: {file.filename}")

    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes)).convert("RGBA")

    sf3d_model = get_sf3d_model()

    with progress("[SF3D] run", 4) as bar:
        image = resize_foreground(image, 0.85)
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
                        bake_resolution=1024,
                        remesh="none",
                        vertex_count=-1,
                    )
        bar.update(1)

        mesh = thicken_mesh_depth(mesh)
        bar.update(1)

        out_mesh_path = os.path.join("temp_outputs", f"{uuid.uuid4()}.glb")
        mesh.export(out_mesh_path, include_normals=True)
        bar.update(1)
    log(f"[SF3D] generated: {out_mesh_path} ({elapsed_seconds(started_at)}).")

    clear_memory()
    return FileResponse(out_mesh_path, media_type="model/gltf-binary", filename="model.glb")


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8000)
