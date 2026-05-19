import re

import torch
from PIL import Image
from qwen_vl_utils import process_vision_info
from transformers import AutoProcessor, Qwen2_5_VLForConditionalGeneration


MODEL_ID = "Qwen/Qwen2.5-VL-3B-Instruct"
FALLBACK_LABEL = "object"

model = None
processor = None


def get_labeler(device: str):
    global model, processor

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
    return words[0] if words else FALLBACK_LABEL


def infer_silhouette_label(image: Image.Image, device: str) -> str:
    qwen_model, qwen_processor = get_labeler(device)
    prompt = (
        "Look at this silhouette image and answer with exactly one English noun "
        "for the animal or object it most resembles. No explanation."
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
            max_new_tokens=8,
            do_sample=False,
        )

    generated_ids = generated_ids[:, inputs.input_ids.shape[1]:]
    output = qwen_processor.batch_decode(
        generated_ids,
        skip_special_tokens=True,
        clean_up_tokenization_spaces=False,
    )[0]
    return clean_label(output)
