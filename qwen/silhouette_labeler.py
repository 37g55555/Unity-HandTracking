import json
import re
import threading

import torch
from PIL import Image
from qwen_vl_utils import process_vision_info
from transformers import AutoProcessor, Qwen2_5_VLForConditionalGeneration


MODEL_ID = "Qwen/Qwen2.5-VL-3B-Instruct"
GENERATION_MAX_SECONDS = 20.0

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
    text = text.strip()
    text = re.sub(r"[^0-9A-Za-z가-힣]", " ", text)
    words = [
        word
        for word in text.split()
        if len(word) > 1 or re.search(r"[가-힣]", word)
    ]
    return " ".join(words[:3])


def parse_label(text: str) -> str:
    text = text.strip()
    text = re.sub(r"^```(?:json)?", "", text, flags=re.IGNORECASE).strip()
    text = re.sub(r"```$", "", text).strip()

    candidates = [text]

    array_start = text.find("[")
    array_end = text.rfind("]")
    if array_start >= 0 and array_end > array_start:
        candidates.append(text[array_start : array_end + 1])

    start = text.find("{")
    end = text.rfind("}")
    if start >= 0 and end > start:
        candidates.append(text[start : end + 1])

    for candidate in candidates:
        try:
            label = extract_label(json.loads(candidate))
        except json.JSONDecodeError:
            continue

        if label:
            return label

    return clean_label(text)


def extract_label(data) -> str:
    if isinstance(data, dict):
        return clean_label(str(data.get("label", "")))

    if isinstance(data, list):
        for item in data:
            if isinstance(item, dict) and item.get("label"):
                return clean_label(str(item["label"]))
            if isinstance(item, str):
                label = clean_label(item)
                if label:
                    return label

    if isinstance(data, str):
        return clean_label(data)

    return ""


def infer_silhouette_label(
    image: Image.Image,
    device: str,
    qwen_model=None,
    qwen_processor=None,
) -> str:
    if qwen_model is None or qwen_processor is None:
        qwen_model, qwen_processor = get_labeler(device)

    prompt = (
        "이 검은 실루엣 이미지를 보고 JSON 객체 하나만 반환하세요. "
        "반드시 label 키 하나만 사용하세요. "
        "이미지는 손 그림자에서 얻은 전체 실루엣이며, 사람 팔이나 손가락 일부가 보여도 전체 외곽이 닮은 대상을 추론하세요. "
        "대상은 동물, 식물, 사물, 자연물 중 가장 가까운 것으로 고르세요. "
        "label 값은 짧은 한국어 명사 하나로만 작성하세요. "
        "사람, 손, 팔, 그림자 같은 촬영 과정 설명은 피하세요. "
        "예시나 마크다운, 분류명, 설명은 포함하지 마세요. "
        "출력 예: {\"label\":\"나무\"}"
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
            max_new_tokens=32,
            max_time=GENERATION_MAX_SECONDS,
            do_sample=False,
        )

    generated_ids = generated_ids[:, inputs.input_ids.shape[1]:]
    output = qwen_processor.batch_decode(
        generated_ids,
        skip_special_tokens=True,
        clean_up_tokenization_spaces=False,
    )[0]
    return parse_label(output)
