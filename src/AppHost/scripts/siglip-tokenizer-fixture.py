"""The reference a tokenizer of ours is held to (phase 13a, P13-1).

SigLIP 2's text tower reads token ids, and a tokenizer that is nearly right
produces ids, produces sixty-four of them, and produces the wrong ones: text
searched against pictures degrades without a sign. So before anything is
embedded in process, the maker's own tokenizer writes down what it gives for
texts a shopper would type, and a test holds ours to it, id for id.

This is the evaluation side of the repository, the one place Python is allowed
(CLAUDE.md, *Do NOT*): it runs once, in a throwaway container, and what it
writes is committed.

    docker run --rm -v "$PWD:/repo" -w /repo python:3.12-slim sh -c \
      "pip install -q transformers==4.49.0 sentencepiece==0.2.0 protobuf==5.29.3 && \
       python src/AppHost/scripts/siglip-tokenizer-fixture.py"

It also leaves the tokenizer's own file where the test looks for it
(models/siglip2/tokenizer.model, outside git): 4 MB of the maker's, at the
pinned revision, Apache-2.0.
"""
import json
import pathlib
import shutil

import transformers
from huggingface_hub import hf_hub_download
from transformers import AutoTokenizer

MODEL = "google/siglip2-base-patch16-224"
REVISION = "75de2d55ec2d0b4efc50b3e9ad70dba96a7b2fa2"
LENGTH = 64  # what SigLIP 2 was trained with: padded to it, cut at it

TEXTS = [
    # What a shopper types, in both of the shop's languages.
    "running shoes",
    "zapatillas running mujer",
    "mochila impermeable para la bici",
    "a backpack to carry my laptop",
    "cafetera italiana de inducción",
    "lámpara de escritorio con carga inalámbrica",
    "sartén antiadherente 24 cm",
    "olla a presión de 6 litros",
    "¿qué puedo regalar a mi mujer?",
    "planta que no sea tóxica para gatos",
    "toallas de baño",
    "mesilla de noche de roble con cajón",
    # What lower-casing and the vocabulary have to get right.
    "ZAPATILLAS",
    "Pulse Runner Carbon",
    "niño, señal, pingüino, camión",
    "azul marino",
    "  two   spaces  and a tab\tinside ",
    "20L 14-inch 1,5 litros 160x230",
    "100% algodón",
    "",
    "a",
    # Something the vocabulary was not written for.
    "🪴 monstera",
    # And a text longer than the window: it is cut, and still ends as the maker ends it.
    "an extendable oak dining table that goes from 140 to 200 centimetres with a centre leaf stored inside and seats six to eight people "
    "comfortably for a long dinner party with friends and family on a winter evening by the fire, and then folds back to its "
    "smaller size the morning after so that the room is a living room again and nobody has to walk around a table for eight",
]

tokenizer = AutoTokenizer.from_pretrained(MODEL, revision=REVISION)
encoded = tokenizer(TEXTS, padding="max_length", max_length=LENGTH, truncation=True)
# The last text is there to be cut: the first one written fitted the window, and held nothing about cutting.
assert len(tokenizer(TEXTS[-1])["input_ids"]) > LENGTH, "the long text is not longer than the window"

out = pathlib.Path("tests/Search.Tests/Images/siglip-tokenizer-fixture.json")
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps({
    "model": MODEL,
    "revision": REVISION,
    "tokenizer": type(tokenizer).__name__,
    "transformers": transformers.__version__,
    "length": LENGTH,
    "pad": tokenizer.pad_token_id,
    "eos": tokenizer.eos_token_id,
    "cases": [{"text": text, "ids": ids} for text, ids in zip(TEXTS, encoded["input_ids"])],
}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

models = pathlib.Path("models/siglip2")
models.mkdir(parents=True, exist_ok=True)
shutil.copyfile(hf_hub_download(MODEL, "tokenizer.model", revision=REVISION), models / "tokenizer.model")
print(f"{len(TEXTS)} texts -> {out}; tokenizer.model -> {models}")
