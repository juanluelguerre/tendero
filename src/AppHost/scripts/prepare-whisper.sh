#!/bin/sh
# Whisper large-v3-turbo and Silero VAD for whisper.cpp, converted by us (phase 14; ADR 0031, 0061).
#
# whisper.cpp reads GGML, and neither maker publishes it. A third party's GGML is
# not what the maker released (ADR 0031 rule 5), so each pinned revision is
# converted with whisper.cpp's own converter at the commit of the server image the
# AppHost runs. Each result is held to the SHA-256 measured the first time; a file
# already there that hashes right is left alone.
#
# The VAD is not optional: on silence or a tone, Whisper writes words that were
# never said ("Gracias.", "Thank you.", "¿Qué?" — the contract suite, 2026-10-16),
# and its no-speech probability says 1e-9 about pure silence. The server listens
# only where Silero hears speech.
#
# This is the evaluation side of the repository, the one place Python is allowed.
# It runs once, in a throwaway container sized for it, from the repository root:
#
#   docker run --rm --memory 10g --cpus 8 -v "$PWD:/repo" -w /repo python:3.12-slim \
#     sh src/AppHost/scripts/prepare-whisper.sh
#
# Weights: Whisper MIT and Silero VAD MIT, at the revisions below (tier 1, checked
# at those revisions on 2026-10-16).
set -eu

MODEL=openai/whisper-large-v3-turbo
REVISION=41f01f3fe87f28c78e2fbf8b568835947dd65ed9
WHISPER_CPP=d1be6fde11ac6e0407606b4e42fe72d34add8037   # v1.9.5, the server image's commit
OPENAI_WHISPER=86098128c0b4f24f0e2aa2994de830614b474227
SILERO=6.2.0                                           # whisper.cpp's own reference at that commit
OUT=models/whisper/ggml-large-v3-turbo-f16.bin
VAD=models/whisper/ggml-silero-v6.2.0.bin
EXPECTED="${EXPECTED:-0dc8bab01bd6b0ab68e8ebb607bf3c960ff1bc136644a23b3ad59f7c00e78d09}"   # measured on 2026-10-16
EXPECTED_VAD="${EXPECTED_VAD:-2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987}"   # measured on 2026-10-16
WORK=/tmp/whisper
RAW=https://raw.githubusercontent.com

ready() { [ -n "$2" ] && [ -f "$1" ] && [ "$(sha256sum "$1" | cut -c1-64)" = "$2" ]; }
held() {
    actual=$(sha256sum "$1" | cut -c1-64)
    if [ -n "$2" ] && [ "$actual" != "$2" ]; then
        echo "$1 hashes $actual, not the $2 measured before. Refused." >&2
        exit 1
    fi
    echo "$actual"
}

if ready "$OUT" "$EXPECTED" && ready "$VAD" "$EXPECTED_VAD"; then
    echo "Whisper and its VAD are ready: $OUT, $VAD."
    exit 0
fi

pip install -q --no-cache-dir torch==2.6.0 torchaudio==2.6.0 --index-url https://download.pytorch.org/whl/cpu
pip install -q --no-cache-dir transformers==4.49.0 huggingface_hub==0.29.3 numpy==2.2.3 silero-vad==$SILERO
rm -rf "$WORK" && mkdir -p "$WORK/openai/whisper/assets" "$WORK/out" "$(dirname "$OUT")"

if ! ready "$OUT" "$EXPECTED"; then
    echo "Preparing $MODEL@$REVISION for whisper.cpp $WHISPER_CPP: download, convert to f16."
    python3 - "$WORK" "$MODEL" "$REVISION" "$WHISPER_CPP" "$OPENAI_WHISPER" <<'PY'
import sys, urllib.request
from huggingface_hub import snapshot_download
work, model, revision, whisper_cpp, openai_whisper = sys.argv[1:]
snapshot_download(model, revision=revision, local_dir=f"{work}/source")
raw = "https://raw.githubusercontent.com"
urllib.request.urlretrieve(f"{raw}/ggml-org/whisper.cpp/{whisper_cpp}/models/convert-h5-to-ggml.py", f"{work}/convert.py")
urllib.request.urlretrieve(f"{raw}/openai/whisper/{openai_whisper}/whisper/assets/mel_filters.npz",
                           f"{work}/openai/whisper/assets/mel_filters.npz")
PY
    python3 "$WORK/convert.py" "$WORK/source" "$WORK/openai" "$WORK/out"
    echo "Whisper: $(held "$WORK/out/ggml-model.bin" "$EXPECTED")"
    mv "$WORK/out/ggml-model.bin" "$OUT"
fi

if ! ready "$VAD" "$EXPECTED_VAD"; then
    echo "Preparing Silero VAD $SILERO for whisper.cpp $WHISPER_CPP."
    python3 -c "import urllib.request; urllib.request.urlretrieve('$RAW/ggml-org/whisper.cpp/$WHISPER_CPP/models/convert-silero-vad-to-ggml.py', '$WORK/convert-vad.py')"
    # The converter names its output itself: <name>-v<version>-ggml.bin.
    python3 "$WORK/convert-vad.py" --output "$WORK/out/silero.bin"
    echo "Silero VAD: $(held "$WORK/out/silero-v$SILERO-ggml.bin" "$EXPECTED_VAD")"
    mv "$WORK/out/silero-v$SILERO-ggml.bin" "$VAD"
fi

rm -rf "$WORK"
echo "Whisper and its VAD are ready: $OUT, $VAD."
