"""Parakeet-TDT-0.6B-v3 as ONNX, exported by us from the maker's weights (phase 14; ADR 0031, 0061).

NVIDIA publishes the model for NeMo and for transformers, not as ONNX, and a third
party's copy is not what the maker released: so the pinned revision is downloaded,
its three networks are exported, and the export is held to the model's own PyTorch
modules before it is kept: the same greedy loop over both, the same words out,
and encoders that agree to float noise.

Into models/parakeet (outside git):

  encoder.onnx (+ .data)  input_features [1, T, 128], attention_mask [1, T] -> encoder_out [1, T/8, 640]
  decoder.onnx            token [1, 1], h [2, 1, 640], c [2, 1, 640]       -> decoder_out [1, 1, 640], h, c
  joint.onnx              decoder_out [1, 640], encoder_frame [1, 640]     -> logits [1, 8198]
  vocab.json              the 8193 pieces, by id (8192 is the blank)
  export.json             what was exported, from what, and how closely it agrees

And into the repository, for the .NET adapter's tests:

  tests/AI.Tests/Speech/parakeet-fixture.json   two LibriSpeech utterances (CC BY 4.0): the audio, the
                                                maker's features for one, and the words the loop hears

The mel features and the TDT loop are written in C# (Speech/Parakeet), so they are
not exported; the fixture is what they are held to.

This is the evaluation side of the repository, the one place Python is allowed.
It runs once, in a throwaway container, from the repository root:

    docker run --rm --memory 12g --cpus 8 -v "$PWD:/repo" -w /repo python:3.12-slim sh -c \
      "apt-get update -qq && apt-get install -y -qq libsndfile1 > /dev/null && \
       pip install -q torch==2.6.0 --index-url https://download.pytorch.org/whl/cpu && \
       pip install -q transformers==5.9.0 librosa==0.11.0 soundfile==0.13.1 datasets==3.6.0 onnx==1.17.0 onnxruntime==1.20.1 && \
       python src/AppHost/scripts/prepare-parakeet.py"

Weights: CC-BY-4.0 at the revision below, accepted for tier 1 by the author on
2026-10-16 (ADR 0061); the attribution is in THIRD-PARTY-NOTICES.md.
"""
import base64
import json
import pathlib

import numpy as np
import onnx
import onnxruntime
import torch
from datasets import load_dataset
from transformers import AutoProcessor, ParakeetForTDT

MODEL = "nvidia/parakeet-tdt-0.6b-v3"
REVISION = "541d1f99c6b0c3cd0b11a95167540bb8edefd82b"
OPSET = 17

out = pathlib.Path("models/parakeet")
out.mkdir(parents=True, exist_ok=True)

processor = AutoProcessor.from_pretrained(MODEL, revision=REVISION)
model = ParakeetForTDT.from_pretrained(MODEL, revision=REVISION).eval()
config = model.config
hidden = config.decoder_hidden_size
layers = config.num_decoder_layers


class Encoder(torch.nn.Module):
    def __init__(self, m):
        super().__init__()
        self.m = m

    def forward(self, input_features, attention_mask):
        return self.m.get_audio_features(input_features=input_features, attention_mask=attention_mask).pooler_output


class Decoder(torch.nn.Module):
    def __init__(self, m):
        super().__init__()
        self.d = m.decoder

    def forward(self, token, h, c):
        embedded = self.d.embedding(token)
        output, (h2, c2) = self.d.lstm(embedded, (h, c))
        return self.d.decoder_projector(output), h2, c2


class Joint(torch.nn.Module):
    def __init__(self, m):
        super().__init__()
        self.j = m.joint

    def forward(self, decoder_out, encoder_frame):
        return self.j(decoder_out, encoder_frame)


samples = load_dataset("hf-internal-testing/librispeech_asr_dummy", "clean", split="validation")
# As 16-bit PCM, the way the fixture keeps them and the shop hears them: every number
# below is of the very samples the .NET tests read back, not of the dataset's floats.
pcm = [(np.clip(np.asarray(samples[i]["audio"]["array"], dtype=np.float32), -1, 1) * 32767).astype("<i2") for i in range(2)]
audios = [p.astype(np.float32) / 32768 for p in pcm]
references = [samples[i]["text"] for i in range(2)]
inputs = processor(audios[0], sampling_rate=16000, return_tensors="pt")

# Exported once: a second run only checks it.
if not (out / "joint.onnx").exists():
    with torch.no_grad():
        torch.onnx.export(
            Encoder(model), (inputs["input_features"], inputs["attention_mask"]), str(out / "encoder.onnx"),
            input_names=["input_features", "attention_mask"], output_names=["encoder_out"],
            dynamic_axes={"input_features": {1: "frames"}, "attention_mask": {1: "frames"}, "encoder_out": {1: "steps"}},
            opset_version=OPSET, external_data=True)
        # One file of weights beside the graph, not a file per tensor.
        whole = onnx.load(str(out / "encoder.onnx"))
        for stray in [path for path in out.iterdir() if path.name not in ("encoder.onnx", "decoder.onnx", "joint.onnx", "vocab.json")]:
            stray.unlink()
        onnx.save_model(whole, str(out / "encoder.onnx"), save_as_external_data=True,
                        all_tensors_to_one_file=True, location="encoder.onnx.data")
        zeros = torch.zeros(layers, 1, hidden)
        torch.onnx.export(
            Decoder(model), (torch.tensor([[config.blank_token_id]]), zeros, zeros), str(out / "decoder.onnx"),
            input_names=["token", "h", "c"], output_names=["decoder_out", "h_out", "c_out"], opset_version=OPSET)
        torch.onnx.export(
            Joint(model), (torch.zeros(1, hidden), torch.zeros(1, hidden)), str(out / "joint.onnx"),
            input_names=["decoder_out", "encoder_frame"], output_names=["logits"], opset_version=OPSET)

vocab = processor.tokenizer.convert_ids_to_tokens(list(range(config.vocab_size - 1))) + ["<blank>"]
(out / "vocab.json").write_text(json.dumps(vocab, ensure_ascii=False))


def maker_words(audio):
    """What transformers' own generate() makes of it: recorded, not the bar (see below)."""
    batch = processor(audio, sampling_rate=16000, return_tensors="pt")
    with torch.no_grad():
        generated = model.generate(**batch)
    sequences = generated.sequences if hasattr(generated, "sequences") else generated
    return processor.batch_decode(sequences, skip_special_tokens=True)[0].strip()


def greedy(frames, predict, joint):
    """The greedy TDT loop the C# adapter runs (TdtGreedy), over whichever networks it is given."""
    state = predict(config.blank_token_id, None)
    tokens, t, emitted = [], 0, 0
    while t < len(frames):
        logits = joint(state, frames[t])
        token = int(np.argmax(logits[:config.vocab_size]))
        duration = config.durations[int(np.argmax(logits[config.vocab_size:]))]
        if token != config.blank_token_id:
            tokens.append(token)
            state = predict(token, state)
            emitted += 1
        if duration == 0 and (token == config.blank_token_id or emitted >= config.max_symbols_per_step):
            duration = 1
        if duration > 0:
            emitted = 0
        t += duration
    return processor.tokenizer.decode(tokens, skip_special_tokens=True).strip()


def torch_words(audio):
    """The loop over the model's own PyTorch modules: what the export must agree with."""
    batch = processor(audio, sampling_rate=16000, return_tensors="pt")
    networks = Decoder(model)
    with torch.no_grad():
        frames = Encoder(model)(batch["input_features"], batch["attention_mask"])[0].numpy()

        def predict(token, state):
            h, c = (torch.zeros(layers, 1, hidden), torch.zeros(layers, 1, hidden)) if state is None else state[1:]
            out_, h2, c2 = networks(torch.tensor([[token]]), h, c)
            return (out_[0], h2, c2)

        def joint(state, frame):
            return model.joint(state[0], torch.from_numpy(frame[None, :]))[0].numpy()

        return greedy(frames, predict, joint), frames


def onnx_words(audio):
    """The same loop over the three ONNX networks."""
    batch = processor(audio, sampling_rate=16000, return_tensors="np")
    encoder = onnxruntime.InferenceSession(str(out / "encoder.onnx"))
    decoder = onnxruntime.InferenceSession(str(out / "decoder.onnx"))
    joint_session = onnxruntime.InferenceSession(str(out / "joint.onnx"))
    frames = encoder.run(None, {"input_features": batch["input_features"].astype(np.float32),
                                "attention_mask": batch["attention_mask"].astype(bool)})[0][0]

    def predict(token, state):
        h, c = (np.zeros((layers, 1, hidden), np.float32),) * 2 if state is None else state[1:]
        return tuple(decoder.run(None, {"token": np.array([[token]], np.int64), "h": h, "c": c}))

    def joint(state, frame):
        return joint_session.run(None, {"decoder_out": state[0][0], "encoder_frame": frame[None, :]})[0][0]

    return greedy(frames, predict, joint), frames


maker = [maker_words(audio) for audio in audios]
by_torch = [torch_words(audio) for audio in audios]
by_onnx = [onnx_words(audio) for audio in audios]
ours = [words for words, _ in by_onnx]
encoder_gap = max(float(np.abs(t[1] - o[1]).max()) for t, o in zip(by_torch, by_onnx))
print("reference:", references)
print("generate: ", maker)
print("torch:    ", [words for words, _ in by_torch])
print("onnx:     ", ours)
print("encoder gap:", encoder_gap)

# The bar is the export, not transformers' generate(): the same loop over the
# PyTorch modules and over the ONNX networks must hear the same words, and the
# encoders must agree to float noise. generate() is recorded beside them. On
# 2026-10-16, run in the same process right after the export, it heard
# "Mr. Kilder … his batter": tracing for the export had left the model in memory
# changed. Run again with the export already on disk, all three heard
# "mister Quilter … his matter", which is what LibriSpeech says.
if [words for words, _ in by_torch] != ours or encoder_gap > 1e-3:
    raise SystemExit("The ONNX export does not hear what the model's own modules hear. Refused.")

features = processor(audios[0], sampling_rate=16000, return_tensors="np")
fixture = {
    "about": "Two LibriSpeech utterances (hf-internal-testing/librispeech_asr_dummy, CC BY 4.0) and what the greedy "
             f"TDT loop over {MODEL}@{REVISION} hears in them, for the .NET adapter's tests (prepare-parakeet.py).",
    "utterances": [
        {"pcm16": base64.b64encode(p.tobytes()).decode(),
         "words": w, "reference": r, "generate": g}
        for p, w, r, g in zip(pcm, ours, references, maker)
    ],
    "features": {
        "frames": int(features["attention_mask"].sum()),
        "first": features["input_features"][0, :8].round(5).tolist(),
        "mean": float(features["input_features"][0].mean()),
    },
    "mel": {"rows": 128, "bins": 257, "sum": float(processor.feature_extractor.mel_filters.sum())},
}
pathlib.Path("tests/AI.Tests/Speech/parakeet-fixture.json").write_text(json.dumps(fixture))
(out / "export.json").write_text(json.dumps(
    {"model": MODEL, "revision": REVISION, "opset": OPSET, "transformers": "5.9.0",
     "encoder_gap": encoder_gap, "hears": ours, "generate": maker, "reference": references}, indent=2))
print("Parakeet is ready:", out)
