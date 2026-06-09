import json
import re
import threading

import torch
from PIL import Image
from qwen_vl_utils import process_vision_info
from transformers import AutoProcessor, Qwen2_5_VLForConditionalGeneration


MODEL_ID = "Qwen/Qwen2.5-VL-3B-Instruct"

model = None
processor = None
labeler_lock = threading.Lock()


def get_labeler(device: str):
    global model, processor

    with labeler_lock:
        if model is None or processor is None:
            dtype = torch.float16 if device == "cuda" else torch.float32
            processor = AutoProcessor.from_pretrained(MODEL_ID)
            model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
                MODEL_ID,
                torch_dtype=dtype,
                device_map="auto" if device == "cuda" else None,
            )
            if device != "cuda":
                model.to(device)
            model.eval()

    return model, processor


def unload_labeler():
    global model, processor

    with labeler_lock:
        if model is not None:
            del model
            model = None

        processor = None

    if torch.cuda.is_available():
        try:
            torch.cuda.synchronize()
        except RuntimeError:
            pass
        torch.cuda.empty_cache()


def clean_label(text: str) -> str:
    text = text.strip().lower()
    text = re.sub(r"[^a-z ]", " ", text)
    words = [word for word in text.split() if len(word) > 1]
    return " ".join(words[:4])


def clean_visual_hint(text: str) -> str:
    text = text.strip().lower()
    text = re.sub(r"[^a-z0-9 ,\-]", " ", text)
    text = re.sub(r"\s+", " ", text).strip(" ,")
    words = text.split()
    return " ".join(words[:12])


def parse_interpretation(text: str) -> dict[str, str]:
    text = text.strip()
    text = re.sub(r"^```(?:json)?", "", text, flags=re.IGNORECASE).strip()
    text = re.sub(r"```$", "", text).strip()

    start = text.find("{")
    end = text.rfind("}")
    if start >= 0 and end > start:
        text = text[start : end + 1]

    try:
        data = json.loads(text)
    except json.JSONDecodeError:
        return {"label": "", "visual_hint": ""}

    return {
        "label": clean_label(str(data.get("label", ""))),
        "visual_hint": clean_visual_hint(str(data.get("visual_hint", ""))),
    }


def infer_silhouette_interpretation(
    image: Image.Image,
    device: str,
    qwen_model=None,
    qwen_processor=None,
) -> dict[str, str]:
    if qwen_model is None or qwen_processor is None:
        qwen_model, qwen_processor = get_labeler(device)

    prompt = (
        "Look at this silhouette image and return only a JSON object with keys label and visual_hint. "
        "This silhouette comes from hand shadow play, so finger-like shapes may be parts of a shadow puppet rather than the intended subject. "
        "Infer the intended animal, plant, or physical object represented by the whole silhouette, not the hand that made it. "
        "label must be exactly one simple English noun. "
        "label must be an animal, a plant including fruit, or a physical object. "
        "Choose the closest non-human match whenever possible. "
        "Avoid hand, finger, glove, palm, arm, or body-part labels unless the whole silhouette is plainly just a real human hand and not a shadow puppet. "
        "visual_hint must be 3 to 8 short English visual words about color, material, structure, and distinctive markings for making a realistic full-color 3D object. "
        "visual_hint must describe the intended subject, not skin, fingers, gloves, or the hand that made the shadow. "
        "Do not include examples, markdown, categories, explanations, or extra keys."
    )
    messages = [
        {
            "role": "user",
            "content": [
                {"type": "image", "image": image.convert("RGB")},
                {"type": "text", "text": prompt},
            ],
        }
    ]

    text = qwen_processor.apply_chat_template(
        messages,
        tokenize=False,
        add_generation_prompt=True,
    )
    image_inputs, video_inputs = process_vision_info(messages)
    inputs = qwen_processor(
        text=[text],
        images=image_inputs,
        videos=video_inputs,
        padding=True,
        return_tensors="pt",
    ).to(qwen_model.device)

    with torch.no_grad():
        generated_ids = qwen_model.generate(
            **inputs,
            max_new_tokens=96,
            do_sample=False,
        )

    generated_ids = generated_ids[:, inputs.input_ids.shape[1]:]
    output = qwen_processor.batch_decode(
        generated_ids,
        skip_special_tokens=True,
        clean_up_tokenization_spaces=False,
    )[0]
    return parse_interpretation(output)
