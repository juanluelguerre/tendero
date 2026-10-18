#!/bin/sh
# The shop's Whisper, quantized by us to q8_0 (phase 14; ADR 0061; docs/analysis/voice-models.md).
#
# The second step after prepare-whisper.sh, which converts the maker's weights to
# f16. It runs in the server image itself, whose whisper-quantize is the one that
# will read the file, so no Python and no other build is involved:
#
#   docker run --rm --memory 2g --cpus 8 -v "$PWD:/repo" -w /repo --entrypoint sh \
#     ghcr.io/ggml-org/whisper.cpp:main-d1be6fde11ac6e0407606b4e42fe72d34add8037 \
#     src/AppHost/scripts/quantize-whisper.sh
#
# q8_0 by the author's ruling of 2026-10-16: it heard all forty recordings exactly
# as f16 did, in 1,266 MiB of the card against 1,990, and beside the rest of the
# shop q5_0 saved nothing measurable. Quantizing is deterministic (run twice on
# 2026-10-16, the same bytes), so the result is held to its hash like the
# conversion; a file already there that hashes right is left alone.
set -eu

FROM=models/whisper/ggml-large-v3-turbo-f16.bin
OUT=models/whisper/ggml-large-v3-turbo-q8_0.bin
EXPECTED="${EXPECTED-31f27efd4672bbaeb845f5f0e626b4e29f9068248acd7fc82cd71cbfca73a8ff}"   # measured on 2026-10-16

if [ -n "$EXPECTED" ] && [ -f "$OUT" ] && [ "$(sha256sum "$OUT" | cut -c1-64)" = "$EXPECTED" ]; then
    echo "Whisper q8_0 is ready: $OUT."
    exit 0
fi

[ -f "$FROM" ] || { echo "$FROM is missing: run prepare-whisper.sh first." >&2; exit 1; }
/app/build/bin/whisper-quantize "$FROM" "$OUT.partial" q8_0 > /dev/null
actual=$(sha256sum "$OUT.partial" | cut -c1-64)
if [ -n "$EXPECTED" ] && [ "$actual" != "$EXPECTED" ]; then
    rm -f "$OUT.partial"
    echo "$OUT hashes $actual, not the $EXPECTED measured before. Refused." >&2
    exit 1
fi
mv "$OUT.partial" "$OUT"
echo "Whisper q8_0: $actual"
echo "Whisper q8_0 is ready: $OUT."
