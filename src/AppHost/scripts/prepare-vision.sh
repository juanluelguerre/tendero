#!/bin/sh
# The vision half of the shop's model, prepared once (ADR 0031, phase 13a, I-2).
#
# Qwen3.5-4B reads pictures: the revision the extractor's model is converted from
# is Qwen3_5ForConditionalGeneration, with a vision tower. llama.cpp serves that
# tower from a second file, the projector (--mmproj), and no maker publishes one.
# ADR 0031 rule 5 refuses a third party's copy, so it is converted here, from the
# same pinned revision and with the same pinned llama.cpp as the text half
# (prepare-extractor.sh), and held to the SHA-256 measured on 2026-10-06. A file
# that is already there and hashes right is left alone; one that does not is
# refused, loudly.
#
# Its own script, not a second output of the extractor's: the download is 8.8 GB
# and only a stack that reads pictures needs it.
set -eu

MODEL=Qwen/Qwen3.5-4B
REVISION=851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a
OUT=/models/qwen3.5-4b-mmproj-f16.gguf
EXPECTED=1a039fd06185e9e28093b7afc19bc6750203801512fa25c9e063ebe6efc0b7f5
WORK=/tmp/vision

if [ -f "$OUT" ] && [ "$(sha256sum "$OUT" | cut -c1-64)" = "$EXPECTED" ]; then
    echo "The model's vision projector is ready: $OUT ($EXPECTED)."
    exit 0
fi

echo "Preparing the vision projector of $MODEL@$REVISION: download, convert to f16."
rm -rf "$WORK" && mkdir -p "$WORK"
hf download "$MODEL" --revision "$REVISION" --local-dir "$WORK/source"
python3 /app/convert_hf_to_gguf.py --mmproj --outtype f16 --outfile "$WORK/mmproj-f16.gguf" "$WORK/source"

ACTUAL=$(sha256sum "$WORK/mmproj-f16.gguf" | cut -c1-64)
if [ "$ACTUAL" != "$EXPECTED" ]; then
    echo "The converted projector hashes $ACTUAL, not the $EXPECTED measured on 2026-10-06. Refused." >&2
    exit 1
fi

mv "$WORK/mmproj-f16.gguf" "$OUT"
rm -rf "$WORK"
echo "The model's vision projector is ready: $OUT ($EXPECTED)."
