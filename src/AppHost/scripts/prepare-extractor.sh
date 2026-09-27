#!/bin/sh
# The claim extractor's model, prepared once (ADR 0018, ADR 0031, P10-0).
#
# No maker publishes a GGUF of Qwen3.5, and ADR 0031 rule 5 refuses a third
# party's copy: so it is downloaded at the revision whose licence was checked,
# converted and quantized with llama.cpp's own tools at a pinned release, and
# held to the SHA-256 P10-0 measured. A file that is already there and hashes
# right is left alone; one that does not is refused, loudly.
set -eu

MODEL=Qwen/Qwen3.5-4B
REVISION=851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a
OUT=/models/qwen3.5-4b-q8_0.gguf
EXPECTED=959b3e3b581774dabfd57dddfb37df22a3609b49b8ea4ae5c082cd756bfd7c59
WORK=/tmp/extractor

if [ -f "$OUT" ] && [ "$(sha256sum "$OUT" | cut -c1-64)" = "$EXPECTED" ]; then
    echo "The extractor's model is ready: $OUT ($EXPECTED)."
    exit 0
fi

echo "Preparing $MODEL@$REVISION: download, convert to f16, quantize to Q8_0."
rm -rf "$WORK" && mkdir -p "$WORK"
hf download "$MODEL" --revision "$REVISION" --local-dir "$WORK/source"
python3 /app/convert_hf_to_gguf.py --outtype f16 --outfile "$WORK/f16.gguf" "$WORK/source"
/app/llama-quantize "$WORK/f16.gguf" "$WORK/q8_0.gguf" Q8_0

ACTUAL=$(sha256sum "$WORK/q8_0.gguf" | cut -c1-64)
if [ "$ACTUAL" != "$EXPECTED" ]; then
    echo "The converted model hashes $ACTUAL, not the $EXPECTED P10-0 measured. Refused." >&2
    exit 1
fi

mv "$WORK/q8_0.gguf" "$OUT"
rm -rf "$WORK"
echo "The extractor's model is ready: $OUT ($EXPECTED)."
