"""OWLv2 as ONNX, converted by us from the maker's weights (the camera search, CS-2; ADR 0031, ADR 0062).

The detector the search box finds the objects of a photograph with. Nobody
publishes it in the format the shop runs, so the pinned revision is downloaded,
exported in two parts, and the export is held to the original before it is
kept: the same texts and the same pictures through both, compared.

Into models/owlv2 (outside git):

  detector.onnx  pixel_values [1, 3, 960, 960], query_embeds [1, q, 512]
                 -> logits [1, 3600, q], boxes [1, 3600, 4] (cx, cy, w, h of the padded square, 0..1)
  text.onnx      input_ids [n, 16] -> text_embeds [n, 512], unit length
  vocab.json, merges.txt   the maker's tokenizer, unchanged
  export.json    what was exported, from what, and how closely it agrees

and into the repository, the reference the .NET side is held to:

  tests/Search.Tests/Images/owlv2-fixture.json   the maker's token ids for texts, and its
                                                 detections in four seed pictures

The kinds are configuration and change with the shop's categories, so the text
tower is exported too and the tokenizer is ours (Owlv2TextTokenizer): no
embedding of a kind is frozen into the export.

This is the evaluation side of the repository, the one place Python is allowed.
It runs once, in a throwaway container:

    docker run --rm --memory 8g --cpus 6 -v "$PWD:/repo" -w /repo python:3.12-slim sh -c \
      "pip install -q torch==2.6.0 --index-url https://download.pytorch.org/whl/cpu && \
       pip install -q transformers==4.49.0 scipy==1.15.1 pillow==11.1.0 onnx==1.17.0 onnxruntime==1.20.1 && \
       python src/AppHost/scripts/prepare-owlv2.py"
"""
import json
import pathlib
import shutil

import numpy as np
import onnxruntime
import torch
from huggingface_hub import hf_hub_download
from PIL import Image
from transformers import Owlv2ForObjectDetection, Owlv2Processor

MODEL = "google/owlv2-base-patch16-ensemble"
REVISION = "cfd3195ba4ea9592eec887ded089f4c08eff231d"
LENGTH = 16
OPSET = 17
PROMPT = "a photo of a {}"

out = pathlib.Path("models/owlv2")
out.mkdir(parents=True, exist_ok=True)

processor = Owlv2Processor.from_pretrained(MODEL, revision=REVISION)
tokenizer = processor.tokenizer
model = Owlv2ForObjectDetection.from_pretrained(MODEL, revision=REVISION).eval()
assert tokenizer.pad_token == "!" and tokenizer.model_max_length == LENGTH, (tokenizer.pad_token, tokenizer.model_max_length)


class Detector(torch.nn.Module):
    """The vision side and the class and box heads, with the queries given."""

    def __init__(self, owl):
        super().__init__()
        self.owl = owl

    def forward(self, pixel_values, query_embeds):
        feature_map, _ = self.owl.image_embedder(pixel_values=pixel_values)
        b, h, w, d = feature_map.shape
        image_feats = feature_map.reshape(b, h * w, d)
        query_mask = torch.ones(query_embeds.shape[:2], dtype=torch.bool)
        logits, _ = self.owl.class_predictor(image_feats, query_embeds, query_mask)
        boxes = self.owl.box_predictor(image_feats, feature_map)
        return logits, boxes


class Text(torch.nn.Module):
    """A query's embedding. No attention mask: the tower is causal and pooled at the end
    marker, so the padding after it changes nothing (held below against the maker's)."""

    def __init__(self, owl):
        super().__init__()
        self.owl = owl

    def forward(self, input_ids):
        embeds = self.owl.owlv2.get_text_features(input_ids=input_ids)
        return embeds / embeds.norm(dim=-1, keepdim=True)


def ids(texts):
    return tokenizer(texts, padding="max_length", max_length=LENGTH, truncation=True, return_tensors="pt")["input_ids"]


# ---------- export ----------
sample = processor(images=Image.new("RGB", (640, 480), (200, 200, 200)), return_tensors="pt")["pixel_values"]
with torch.no_grad():
    torch.onnx.export(Text(model), (torch.zeros(1, LENGTH, dtype=torch.long),), str(out / "text.onnx"), opset_version=OPSET, dynamo=False,
                      input_names=["input_ids"], output_names=["text_embeds"],
                      dynamic_axes={"input_ids": {0: "n"}, "text_embeds": {0: "n"}})
    torch.onnx.export(Detector(model), (sample, torch.zeros(1, 2, 512)), str(out / "detector.onnx"), opset_version=OPSET, dynamo=False,
                      input_names=["pixel_values", "query_embeds"], output_names=["logits", "boxes"],
                      dynamic_axes={"query_embeds": {1: "queries"}, "logits": {2: "queries"}})

for name in ("vocab.json", "merges.txt"):
    shutil.copyfile(hf_hub_download(MODEL, name, revision=REVISION), out / name)

# ---------- held to the original ----------
seed = json.loads(pathlib.Path("seed/products.sample.json").read_text(encoding="utf-8"))
pictures = {}
for product in seed:
    if product["item_id"] in ("B073WXYZ01", "B08JKLM202", "B06GHIJ505", "B16FUR1303"):
        first = product["images"][0]
        pictures[product["item_id"]] = pathlib.Path("seed") / (first if isinstance(first, str) else first["file"])
words = ["sneaker", "backpack", "desk lamp", "sofa", "bottle"]
prompts = [PROMPT.format(w) for w in words]

text_session = onnxruntime.InferenceSession(str(out / "text.onnx"), providers=["CPUExecutionProvider"])
detector_session = onnxruntime.InferenceSession(str(out / "detector.onnx"), providers=["CPUExecutionProvider"])

worst_text, worst_logit, worst_box = 0.0, 0.0, 0.0
detections = []
with torch.no_grad():
    for item, path in pictures.items():
        picture = Image.open(path).convert("RGB")
        inputs = processor(text=prompts, images=picture, return_tensors="pt")
        original = model(**inputs)
        original_queries, _, _ = model.image_text_embedder(
            input_ids=inputs["input_ids"], pixel_values=inputs["pixel_values"], attention_mask=inputs["attention_mask"])

        queries = text_session.run(None, {"input_ids": ids(prompts).numpy()})[0]
        logits, boxes = detector_session.run(None, {"pixel_values": inputs["pixel_values"].numpy(), "query_embeds": queries[None, :, :]})
        worst_text = max(worst_text, float(np.abs(queries - original_queries.reshape(len(prompts), -1).numpy()).max()))
        worst_logit = max(worst_logit, float(np.abs(logits - original.logits.numpy()).max()))
        worst_box = max(worst_box, float(np.abs(boxes - original.pred_boxes.numpy()).max()))

        maker_logits, maker_boxes = original.logits[0].numpy(), original.pred_boxes[0].numpy()
        found = []
        for at, word in enumerate(words):
            best = int(maker_logits[:, at].argmax())
            found.append({"words": word, "logit": round(float(maker_logits[best, at]), 4),
                          "box": [round(float(v), 5) for v in maker_boxes[best]]})
        detections.append({"file": str(path).replace("\\", "/"), "width": picture.width, "height": picture.height, "found": found})

agreement = {"text_max_abs_difference": worst_text, "logits_max_abs_difference": worst_logit, "boxes_max_abs_difference": worst_box}
assert worst_text < 1e-4 and worst_logit < 0.05 and worst_box < 1e-3, agreement

# ---------- the reference ----------
texts = [
    "a photo of a desk lamp", "a photo of a t-shirt", "A Photo Of A SNEAKER", "chest of drawers",
    "plate or bowl or drinking glass", "it's a   kettle!", "  leading and trailing spaces  ",
    "2 plates, 3 cups", "wi-fi speaker", "café", "", "a photo of a very long query that runs past the window of sixteen tokens",
    "headphones", "loudspeaker", "computer mouse", "teddy bear", "figurine", "picture frame", "planter", "towel rail",
]
fixture = pathlib.Path("tests/Search.Tests/Images/owlv2-fixture.json")
fixture.write_text(json.dumps({
    "model": MODEL, "revision": REVISION, "length": LENGTH, "prompt": PROMPT,
    "note": "The maker's own numbers, PyTorch and its processor, not the export: token ids, and each query's best patch.",
    "tokens": [{"text": text, "ids": [int(v) for v in row]} for text, row in zip(texts, ids(texts).tolist())],
    "detections": detections,
}, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")

(out / "export.json").write_text(json.dumps({
    "model": MODEL, "revision": REVISION, "opset": OPSET, "torch": torch.__version__,
    "image_size": processor.image_processor.size, "mean": processor.image_processor.image_mean, "std": processor.image_processor.image_std,
    "files": {name: (out / name).stat().st_size for name in ("detector.onnx", "text.onnx", "vocab.json", "merges.txt")},
    "agreement_with_the_original": agreement,
}, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"agreement": agreement, "files": {name: (out / name).stat().st_size for name in ("detector.onnx", "text.onnx")}}, indent=1))
