"""SigLIP 2's two towers as ONNX, converted by us from the maker's weights (phase 13a, P13-2; ADR 0031).

Nobody publishes this model in the format the shop runs, and a third party's
copy is not what the maker released: so the pinned revision is downloaded,
each tower is exported, and the export is held to the original before it is
kept — the same texts and the same pictures through both, compared.

Three things come out, into models/siglip2 (outside git):

  vision.onnx   pixel_values [n, 3, 224, 224] -> image_embeds [n, 768]
  text.onnx     input_ids    [n, 64]          -> text_embeds  [n, 768]
  export.json   what was exported, from what, and how closely it agrees

and two into the repository, for tests and for the record:

  tests/Search.Tests/Images/siglip-embedding-fixture.json   the maker's embeddings a .NET adapter is held to
  artifacts/siglip-lowercase.json                           text against the seed's pictures, as typed and lower-cased

This is the evaluation side of the repository, the one place Python is allowed.
It runs once, in a throwaway container:

    docker run --rm --memory 10g --cpus 8 -v "$PWD:/repo" -w /repo python:3.12-slim sh -c \
      "pip install -q torch==2.6.0 --index-url https://download.pytorch.org/whl/cpu && \
       pip install -q transformers==4.49.0 sentencepiece==0.2.0 protobuf==5.29.3 pillow==11.1.0 onnx==1.17.0 onnxruntime==1.20.1 && \
       python src/AppHost/scripts/prepare-siglip.py"
"""
import json
import pathlib

import numpy as np
import onnxruntime
import torch
from PIL import Image
from transformers import AutoImageProcessor, AutoModel, AutoTokenizer

MODEL = "google/siglip2-base-patch16-224"
REVISION = "75de2d55ec2d0b4efc50b3e9ad70dba96a7b2fa2"
LENGTH = 64
OPSET = 17

out = pathlib.Path("models/siglip2")
out.mkdir(parents=True, exist_ok=True)

model = AutoModel.from_pretrained(MODEL, revision=REVISION).eval()
# Each half loaded by itself: at this version the combined processor builds the first SigLIP's tokenizer
# for a checkpoint that ships Gemma's, and fails. The tokenizer is the one the fixture was written with.
tokenizer = AutoTokenizer.from_pretrained(MODEL, revision=REVISION)
image_processor = AutoImageProcessor.from_pretrained(MODEL, revision=REVISION)


class Vision(torch.nn.Module):
    def __init__(self, siglip):
        super().__init__()
        self.siglip = siglip

    def forward(self, pixel_values):
        return self.siglip.get_image_features(pixel_values=pixel_values)


class Text(torch.nn.Module):
    def __init__(self, siglip):
        super().__init__()
        self.siglip = siglip

    def forward(self, input_ids):
        # No attention mask: SigLIP was trained on text padded to the window, and reads the padding.
        return self.siglip.get_text_features(input_ids=input_ids)


def tokens(texts):
    return tokenizer(texts, padding="max_length", max_length=LENGTH, truncation=True, return_tensors="pt")["input_ids"]


def pixels(paths):
    return image_processor([Image.open(path).convert("RGB") for path in paths], return_tensors="pt")["pixel_values"]


def unit(vectors):
    return vectors / np.linalg.norm(vectors, axis=1, keepdims=True)


# ---------- export ----------
with torch.no_grad():
    torch.onnx.export(Vision(model), (torch.zeros(1, 3, 224, 224),), str(out / "vision.onnx"), opset_version=OPSET, dynamo=False,
                      input_names=["pixel_values"], output_names=["image_embeds"],
                      dynamic_axes={"pixel_values": {0: "n"}, "image_embeds": {0: "n"}})
    torch.onnx.export(Text(model), (torch.zeros(1, LENGTH, dtype=torch.long),), str(out / "text.onnx"), opset_version=OPSET, dynamo=False,
                      input_names=["input_ids"], output_names=["text_embeds"],
                      dynamic_axes={"input_ids": {0: "n"}, "text_embeds": {0: "n"}})

# ---------- held to the original ----------
seed = json.loads(pathlib.Path("seed/products.sample.json").read_text(encoding="utf-8"))
catalogue = []
for product in seed:
    first = product["images"][0]
    path = pathlib.Path("seed") / (first if isinstance(first, str) else first["file"])
    if path.exists():
        catalogue.append((product["item_id"], product["name"]["en"], product["name"]["es"], path))

reference_pictures = [entry for entry in catalogue if entry[0] in ("B073WXYZ01", "B13BAG0711", "B16PLA1401", "B16FUR1303")]
reference_texts = ["running shoes", "a green bike pannier", "una planta de interior", "sofá gris de dos plazas", "ZAPATILLAS", ""]

vision_session = onnxruntime.InferenceSession(str(out / "vision.onnx"), providers=["CPUExecutionProvider"])
text_session = onnxruntime.InferenceSession(str(out / "text.onnx"), providers=["CPUExecutionProvider"])

with torch.no_grad():
    picture_inputs = pixels([entry[3] for entry in reference_pictures])
    text_inputs = tokens(reference_texts)
    original_pictures = model.get_image_features(pixel_values=picture_inputs).numpy()
    original_texts = model.get_text_features(input_ids=text_inputs).numpy()
exported_pictures = vision_session.run(None, {"pixel_values": picture_inputs.numpy()})[0]
exported_texts = text_session.run(None, {"input_ids": text_inputs.numpy()})[0]

agreement = {
    "vision_max_abs_difference": float(np.abs(original_pictures - exported_pictures).max()),
    "text_max_abs_difference": float(np.abs(original_texts - exported_texts).max()),
    "vision_min_cosine": float((unit(original_pictures) * unit(exported_pictures)).sum(axis=1).min()),
    "text_min_cosine": float((unit(original_texts) * unit(exported_texts)).sum(axis=1).min()),
}
assert agreement["vision_min_cosine"] > 0.9999 and agreement["text_min_cosine"] > 0.9999, agreement

fixture = pathlib.Path("tests/Search.Tests/Images/siglip-embedding-fixture.json")
fixture.write_text(json.dumps({
    "model": MODEL, "revision": REVISION, "dimensions": int(original_pictures.shape[1]),
    "note": "The maker's own embeddings, unit length, six decimals: PyTorch, not the export.",
    "pictures": [{"file": str(entry[3]).replace("\\", "/"), "embedding": [round(float(v), 6) for v in vector]}
                 for entry, vector in zip(reference_pictures, unit(original_pictures))],
    "texts": [{"text": text, "embedding": [round(float(v), 6) for v in vector]} for text, vector in zip(reference_texts, unit(original_texts))],
}, ensure_ascii=False) + "\n", encoding="utf-8")

# ---------- lower-case or not: text against the seed's own pictures ----------
def embed_pictures(paths, batch=16):
    vectors = []
    for start in range(0, len(paths), batch):
        vectors.append(vision_session.run(None, {"pixel_values": pixels(paths[start:start + batch]).numpy()})[0])
    return unit(np.concatenate(vectors))


def embed_texts(texts, batch=32):
    vectors = []
    for start in range(0, len(texts), batch):
        vectors.append(text_session.run(None, {"input_ids": tokens(texts[start:start + batch]).numpy()})[0])
    return unit(np.concatenate(vectors))


picture_vectors = embed_pictures([entry[3] for entry in catalogue])


def retrieval(texts):
    scores = embed_texts(texts) @ picture_vectors.T
    ranks = [int((row > row[index]).sum()) + 1 for index, row in enumerate(scores)]
    return {"recall_at_1": round(sum(rank == 1 for rank in ranks) / len(ranks), 3),
            "recall_at_5": round(sum(rank <= 5 for rank in ranks) / len(ranks), 3),
            "mean_reciprocal_rank": round(sum(1 / rank for rank in ranks) / len(ranks), 3)}


english = [entry[1] for entry in catalogue]
spanish = [entry[2] for entry in catalogue]
lowercase = {
    "pictures": len(catalogue),
    "what": "each product's name as the query, its own picture as the answer, among every seed picture",
    "en_as_written": retrieval(english), "en_lower_cased": retrieval([text.lower() for text in english]),
    "en_upper_cased": retrieval([text.upper() for text in english]),
    "es_as_written": retrieval(spanish), "es_lower_cased": retrieval([text.lower() for text in spanish]),
    "es_upper_cased": retrieval([text.upper() for text in spanish]),
}
pathlib.Path("artifacts").mkdir(exist_ok=True)
pathlib.Path("artifacts/siglip-lowercase.json").write_text(json.dumps(lowercase, indent=2) + "\n", encoding="utf-8")

(out / "export.json").write_text(json.dumps({
    "model": MODEL, "revision": REVISION, "opset": OPSET, "torch": torch.__version__,
    "files": {name: (out / name).stat().st_size for name in ("vision.onnx", "text.onnx")},
    "agreement_with_the_original": agreement,
}, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"agreement": agreement, "lowercase": lowercase,
                  "files": {name: (out / name).stat().st_size for name in ("vision.onnx", "text.onnx")}}, indent=1))
