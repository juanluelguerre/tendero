# Third-party notices

Model weights the shop runs, converted by us from their makers' pinned revisions
(ADR 0031) and kept outside git, under `models/`. Each licence was read at the
revision named, and each entry says what we changed. The code dependencies'
licences are recorded beside their versions in `Directory.Packages.props` and the
front end's `package.json`.

## Speech (phase 14, ADR 0061)

| Weights | Revision | Licence | What we changed |
|---|---|---|---|
| [NVIDIA Parakeet-TDT-0.6B-v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3), © NVIDIA | `541d1f99c6b0c3cd0b11a95167540bb8edefd82b` | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Exported to ONNX as three networks (encoder, predictor, joint), unchanged in their weights (`src/AppHost/scripts/prepare-parakeet.py`) |
| [OpenAI Whisper large-v3-turbo](https://huggingface.co/openai/whisper-large-v3-turbo), © OpenAI | `41f01f3fe87f28c78e2fbf8b568835947dd65ed9` | MIT | Converted to GGML f16 with whisper.cpp's converter (`src/AppHost/scripts/prepare-whisper.sh`), then quantized to q8_0 with its `whisper-quantize` (`quantize-whisper.sh`) |
| [Silero VAD](https://github.com/snakers4/silero-vad) 6.2.0, © Silero Team | the `silero-vad` 6.2.0 package | MIT | Converted to GGML with whisper.cpp's converter (`src/AppHost/scripts/prepare-whisper.sh`) |

The test fixture `tests/AI.Tests/Speech/parakeet-fixture.json` holds two
utterances of [LibriSpeech](https://www.openslr.org/12) (V. Panayotov, G. Chen,
D. Povey and S. Khudanpur), from `hf-internal-testing/librispeech_asr_dummy`,
under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), unchanged but for
their encoding as 16-bit PCM.

## Objects in a photograph (the camera search, ADR 0062)

| Weights | Revision | Licence | What we changed |
|---|---|---|---|
| [Google OWLv2 base, patch 16, ensemble](https://huggingface.co/google/owlv2-base-patch16-ensemble), © Google | `cfd3195ba4ea9592eec887ded089f4c08eff231d` | Apache-2.0 | Exported to ONNX as two networks (the text tower; the vision tower with its class and box heads), unchanged in their weights; the tokenizer's vocabulary and merges copied as they are (`src/AppHost/scripts/prepare-owlv2.py`) |
