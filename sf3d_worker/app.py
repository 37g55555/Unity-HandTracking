import gc
import io
import os
import uuid
from contextlib import nullcontext

import cv2
import numpy as np
import rembg
import torch
from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.responses import FileResponse, StreamingResponse
from PIL import Image, ImageEnhance

from sf3d.system import SF3D
from sf3d.utils import get_device, remove_background, resize_foreground


app = FastAPI(
    title="SF3D Unity API Server",
    description="Receives Unity shadow PNGs and returns GLB models.",
)

device = get_device()
if not (torch.cuda.is_available() or torch.backends.mps.is_available()):
    device = "cpu"

model = None
rembg_session = None
controlnet = None
cn_pipe = None
texture_resolution = int(os.getenv("SF3D_TEXTURE_RESOLUTION", "512"))
sf3d_depth_axis = os.getenv("SF3D_DEPTH_AXIS", "z").lower()
sf3d_min_depth_ratio = float(os.getenv("SF3D_MIN_DEPTH_RATIO", "0.70"))
sf3d_max_depth_scale = float(os.getenv("SF3D_MAX_DEPTH_SCALE", "6.0"))

os.makedirs("temp_outputs", exist_ok=True)


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
    texture_rgba.putalpha(mask_image)
    return texture_rgba


def brighten_storybook_texture(texture: Image.Image) -> Image.Image:
    texture_rgb = texture.convert("RGB")
    texture_rgb = ImageEnhance.Color(texture_rgb).enhance(1.18)
    texture_rgb = ImageEnhance.Brightness(texture_rgb).enhance(1.08)
    texture_rgb = ImageEnhance.Contrast(texture_rgb).enhance(1.06)
    return texture_rgb


def thicken_mesh_for_asteroid(mesh):
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
        print(
            f"SF3D depth unchanged: extents={np.round(extents, 4).tolist()}",
            flush=True,
        )
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

    print(
        "SF3D depth thickened: "
        f"axis={depth_axis}, scale={depth_scale:.3f}, "
        f"before={np.round(extents, 4).tolist()}, "
        f"after={np.round(mesh.extents, 4).tolist()}",
        flush=True,
    )
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
    global controlnet, cn_pipe
    if cn_pipe is not None:
        print("Unloading ControlNet texture pipeline from CUDA memory...", flush=True)
        del cn_pipe
        cn_pipe = None
    if controlnet is not None:
        del controlnet
        controlnet = None
    clear_memory()


def unload_sf3d_model():
    global model
    if model is not None:
        print("Unloading SF3D model from CUDA memory...", flush=True)
        del model
        model = None
    clear_memory()


def get_rembg_session():
    global rembg_session
    if rembg_session is None:
        rembg_session = rembg.new_session()
    return rembg_session


def get_sf3d_model():
    global model
    if model is None:
        unload_texture_pipeline()
        print(f"Loading SF3D model on device: {device}...", flush=True)
        try:
            model = SF3D.from_pretrained(
                "stabilityai/stable-fast-3d",
                config_name="config.yaml",
                weight_name="model.safetensors",
            )
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
        model.eval()
        print("SF3D model loaded.", flush=True)
    return model


def get_texture_pipeline():
    global controlnet, cn_pipe
    if cn_pipe is None:
        unload_sf3d_model()
        from diffusers import (
            ControlNetModel,
            StableDiffusionControlNetPipeline,
            UniPCMultistepScheduler,
        )

        dtype = torch.float16 if device == "cuda" else torch.float32
        print("Loading ControlNet texture pipeline...", flush=True)
        controlnet = ControlNetModel.from_pretrained(
            "lllyasviel/sd-controlnet-canny",
            torch_dtype=dtype,
        )
        cn_pipe = StableDiffusionControlNetPipeline.from_pretrained(
            "runwayml/stable-diffusion-v1-5",
            controlnet=controlnet,
            torch_dtype=dtype,
        )
        cn_pipe.scheduler = UniPCMultistepScheduler.from_config(cn_pipe.scheduler.config)
        if device == "cuda":
            cn_pipe.to(device)
        else:
            cn_pipe.to(device)
        print("ControlNet texture pipeline loaded.", flush=True)
    return cn_pipe


@app.get("/health")
async def health():
    return {
        "ok": True,
        "device": device,
        "cuda": torch.cuda.is_available(),
        "sf3d_loaded": model is not None,
    }


@app.post("/generate-texture")
async def generate_texture(file: UploadFile = File(...)):
    print(f"Received texture request: {file.filename}", flush=True)

    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes))
    silhouette_mask = extract_silhouette_mask(image)
    control_image = build_control_image(silhouette_mask)
    silhouette_mask.save("temp_outputs/last_silhouette_mask.png", format="PNG")
    control_image.save("temp_outputs/last_control_edges.png", format="PNG")

    prompt = (
        "A whimsical miniature rocky planet texture inspired by a poetic children's storybook, "
        "soft watercolor and gouache, visible stone surface, subtle craters, gentle rock grain, "
        "rounded asteroid planet, warm beige stone, dusty rose, muted teal, pale yellow, "
        "soft gradient color bands, hand-painted brush strokes, delicate star speckles, "
        "charming but still planet-like, centered single object inside the provided silhouette, "
        "transparent background, 3D asset texture"
    )
    negative_prompt = (
        "low quality, worst quality, text, watermark, extra objects, duplicate object, "
        "background scenery, dark, dull, muddy colors, black texture, monochrome, gloomy, horror, "
        "neon, rainbow, candy, plastic, flat poster, overly saturated, pure gray asteroid, "
        "realistic dirty stone"
    )

    pipe = get_texture_pipeline()
    print("Running ControlNet inference from shadow silhouette mask...", flush=True)
    output = pipe(
        prompt,
        image=control_image,
        negative_prompt=negative_prompt,
        height=texture_resolution,
        width=texture_resolution,
        num_inference_steps=20,
        guidance_scale=7.5,
        controlnet_conditioning_scale=1.25,
    ).images[0]
    output = brighten_storybook_texture(output)
    output = apply_silhouette_alpha(output, silhouette_mask)

    img_byte_arr = io.BytesIO()
    output.save(img_byte_arr, format="PNG")
    img_byte_arr.seek(0)
    output.save("temp_outputs/last_texture.png", format="PNG")

    unload_texture_pipeline()
    return StreamingResponse(img_byte_arr, media_type="image/png")


@app.post("/generate-3d")
async def generate_3d(file: UploadFile = File(...)):
    print(f"Received 3D request: {file.filename}", flush=True)

    image_bytes = await file.read()
    image = Image.open(io.BytesIO(image_bytes)).convert("RGBA")

    print("Preprocessing image...", flush=True)
    image = remove_background(image, get_rembg_session())
    image = resize_foreground(image, 0.85)

    print("Running SF3D inference...", flush=True)
    sf3d_model = get_sf3d_model()
    autocast_context = (
        torch.autocast(device_type=device, dtype=torch.bfloat16)
        if device == "cuda"
        else nullcontext()
    )
    with torch.no_grad():
        with autocast_context:
            mesh, _ = sf3d_model.run_image(
                [image],
                bake_resolution=1024,
                remesh="none",
                vertex_count=-1,
            )

    mesh = thicken_mesh_for_asteroid(mesh)

    out_mesh_path = os.path.join("temp_outputs", f"{uuid.uuid4()}.glb")
    mesh.export(out_mesh_path, include_normals=True)
    print(f"Generated successfully: {out_mesh_path}", flush=True)

    clear_memory()
    return FileResponse(out_mesh_path, media_type="model/gltf-binary", filename="model.glb")


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8000)
