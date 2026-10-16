# Voice models — what was measured, and what it leaves to choose

Phase 14, V-5 (`docs/specs/voice.md`). The author's forty recordings
(`docs/analysis/voice-recordings.md`, twenty a language) through every engine
behind `ISpeechToText`, with the tool that measures any adapter the same way:

```bash
node tools/voice-eval/prepare.mjs     # the recordings as the shop's 16 kHz mono WAV, into artifacts/voice-eval
dotnet run --project tools/SearchEval -- --suite voice --whisper http://localhost:38086 --parakeet models/parakeet
```

Measured on 2026-10-16 on one laptop: an RTX 5070 Laptop (8 GB) and the CPU
behind it, one question at a time, the first question asked once to warm the
model and not counted. **Word error rate** is the words changed, dropped and added
over the words said, both sides normalised alike (lowercase, no punctuation,
numbers as figures, accents folded, `€` as *euros*: what a search would see).
**Latency** is the whole call, from the WAV sent to the words back — the time the
shopper waits after they stop speaking.

## The table

| engine · model | where | es WER | en WER | exact lines (of 40) | median | p90 | real-time factor | memory at peak |
|---|---|---:|---:|---:|---:|---:|---:|---|
| whisper.cpp · large-v3-turbo f16 | GPU | 4.5 % | 17.3 % | 27 | 0.41–0.44 s | 0.43–0.46 s | 0.14 | 605 MiB RAM, 1,990 MiB GPU |
| whisper.cpp · large-v3-turbo **q8_0** | GPU | 4.5 % | 17.3 % | 27 | 0.41–0.44 s | 0.43–0.47 s | 0.14 | 683 MiB RAM, 1,266 MiB GPU |
| whisper.cpp · large-v3-turbo q5_0 | GPU | 4.5 % | 17.3 % | 26 | 0.41–0.42 s | 0.43–0.44 s | 0.14 | 700 MiB RAM, 980 MiB GPU |
| whisper.cpp · small f16 | GPU | 9.8 % | 26.9 % | 19 | 0.19–0.21 s | 0.23–0.24 s | 0.07–0.09 | 614 MiB RAM, 944 MiB GPU |
| whisper.cpp · large-v3-turbo f16 | CPU, 8 cores | 4.5 % | 17.3 % | 27 | 10.8–12.9 s | 11.6–17.1 s | 3.7–4.2 | ~1.7 GiB |
| whisper.cpp · large-v3-turbo q8_0 | CPU, 8 cores | 4.5 % | 17.3 % | 27 | 7.4 s | 7.5 s | 2.4–2.6 | 1,014 MiB |
| whisper.cpp · large-v3-turbo q5_0 | CPU, 8 cores | 4.5 % | 16.3 % | 27 | 9.5–9.6 s | 9.6–10.6 s | 3.1–3.4 | 727 MiB |
| whisper.cpp · small f16 | CPU, 8 cores | 9.8 % | 26.0 % | 20 | 1.65–1.77 s | 1.86 s | 0.54–0.61 | 692 MiB |
| Parakeet-TDT-0.6B-v3 · ONNX, in process | CPU | 5.4 % | 20.2 % | 26 | 3.0–3.7 s | 3.3–4.1 s | 1.0–1.3 | 2.51 GiB (the whole process) |
| `web-speech` · Chrome on the device | — | not measured | | | | | | |

Where a cell has a range, the two languages differ; the rest are the same in
both. The whisper.cpp server is the pinned image (`main-` and `main-cuda-` at
v1.9.5's commit) with Silero VAD in front and `--threads 8`, in a container held
to 3 GB and 8 CPUs as the AppHost runs it; its memory is `docker stats` sampled
once a second, and the GPU's is `nvidia-smi` with nothing else on the card.
Parakeet's two CPU rows are two runs (alone, and after Whisper in the same
process); the slower one is the one the shop would see on a busy machine.

## What it says

1. **On a GPU, turbo is the engine, and quantizing it costs nothing measurable.**
   q8_0 heard every line exactly as f16 did, in the same 0.44 s, in 64 % of the
   card's memory; q5_0 lost one line in English for another 290 MiB. The card
   Tino and Steward share is 8 GB, so the 700 MiB that q8_0 gives back is real.
2. **On the CPU, nothing that hears well answers in two seconds** — ruling 4's
   bar. Turbo quantized, the model the ruling names, takes 7.4 s (q8_0) and
   9.6 s (q5_0): **q8_0 is faster than q5_0 on this CPU**, because unpacking five
   bits costs more than reading eight. Parakeet takes 3–3.7 s. Only `small` is
   under two seconds (1.65–1.77 s), with twice the errors. By ruling 4 as
   written, the shop's engine is **off on a machine with no GPU, and says so**,
   as Tino does.
3. **Parakeet is the CPU's best engine, and not the GPU's.** On the CPU it is
   two to 2.5 times faster than turbo q8_0, for one more error in Spanish (5.4 % against
   4.5 %) and three points more in English (20.2 % against 17.3 %). In process it has no GPU path here:
   ONNX Runtime's DirectML or CUDA would be a new provider, a decision rather than
   a flag.
4. **The ruling that dropped `small` holds.** Its premise was that Parakeet is the
   better small, fast engine. On the CPU, small is twice as fast as Parakeet and
   makes nearly twice its errors in Spanish (9.8 % against 5.4 %): *pues una
   mochila*, *Cafetena*, *un quise cocina*, *¿Tenías…?*. Measured once to check
   the premise, not as a candidate.
5. **English is three to four times harder than Spanish for every engine**, and it is the
   same lines each time: *a desk lamp* (*Dexilamp*, *Adex lamp*), *reading light*
   (*really like*), *warm clothes for cold weather* (*one cloth for cold water*,
   *Warren Club*), *a kitchen kit… under two hundred euros* (*K10 kit… 200 UDUPS*).
   When every engine misses a line the same way, the line — how it sounds in this
   recording — is the cause more than the engine, and a better model will not
   move it much. What can is the shop telling the engine what it sells: Whisper
   takes a prompt (the catalogue's names), Parakeet does not. Not tried yet.
6. **One reference is incomplete.** es-15 was recorded as *"Pues una mochila por
   menos de setenta euros"* and the list says *"Mochila por menos de setenta
   euros"*. Every engine heard the *pues*, so every engine is charged two errors
   it did not make. Left as recorded so the numbers stay comparable; a re-recorded
   es-15 or a corrected line lowers every Spanish WER by 1.8 points (turbo from
   4.5 % to 2.7 %: two of its five Spanish errors are this line).

## On one card with the rest of the shop

The table above measured the speech model on an empty card. The shop puts more
on it: Steward's and Tino's model (Qwen3.5-4B q8_0 on llama.cpp `server-cuda-v0.5.0`,
the AppHost's arguments), its vision projector when `Tendero__Vision` is on, and
search's embedder (bge-m3) and reranker (bge-reranker-v2-m3) on TEI. Measured on
the same 8,151 MiB card the same evening, each configuration loaded fresh. *Busy*
means the LLM writing 400-token answers back to back while the forty questions
are heard, and, where said, the embedder and the reranker answering batches of
sixteen texts without a pause, which is far more than a shopper causes.

| on the card | speech model | card used | voice, LLM idle (median) | voice, all busy (median · p90) | LLM alone → beside the voice |
|---|---|---:|---:|---:|---|
| LLM + speech | q5_0 | 5,779 MiB | 0.15–0.16 s | 0.31–0.35 s · 0.34–0.39 s | 51–63 → 40–45 tok/s |
| LLM + speech | q8_0 | 6,065 MiB | 0.16–0.17 s | 0.31–0.34 s · 0.35–0.37 s | 51–63 → 41–44 tok/s |
| LLM + vision + speech | q8_0 | 6,931 MiB | 0.15–0.17 s | 0.31–0.35 s · 0.33–0.38 s | 51–63 → 40–45 tok/s |
| all of it, search idle | q5_0 | 7,858 MiB | 0.16–0.17 s | 0.72–0.82 s · 0.85–1.0 s | 50–63 → 12 tok/s, 42 once the voice stops |
| all of it, search idle | q8_0 | 7,806 MiB | 0.16–0.17 s | 0.73–0.89 s · 0.88–0.95 s | 50–63 → 11 tok/s, 62 once the voice stops |
| all of it, search busy | q5_0 | 7,867 MiB | 0.15–0.17 s | 0.23–0.24 s · 1.3–1.8 s | 51–64 → 13 tok/s |
| all of it, search busy | q8_0 | 7,847 MiB | 0.16–0.17 s | 1.3–1.5 s · 1.6–2.9 s | 51–64 → 7–48 tok/s |

What it says:

1. **q5_0 against q8_0 is not the question.** Beside the LLM they differ by
   286 MiB and answer alike. With everything loaded, both reach the same ceiling
   (7.8–7.9 GB of the card's 8.15), where the driver stops giving more and the
   models slow down, whichever file is loaded. The 286 MiB does not move that line.
2. **LLM, vision and speech fit with a gigabyte to spare** (6.9 GB), and the voice
   stays at a third of a second while the LLM writes. That is Tino's real case.
3. **The whole shop on one 8 GB card is full.** The search models alone hold 2.7 GB,
   and with them nothing fails, but everything slows down. The voice still answers
   in under a second with search idle. The LLM falls from about 55 tok/s to about
   12 for as long as the voice is being heard, and recovers after. Under the busy
   search's artificial load, p90 reaches 1.3–2.9 s.
4. **The first table's 0.44 s on the GPU was the card waking up.** With the LLM
   loaded, the same questions came back in 0.15–0.17 s. A card with nothing on it
   drops its clocks between questions; one that holds a model does not.

## The browser's engine, and why it has no row

Chrome 155 has on-device recognition (`SpeechRecognition.available({ langs,
processLocally: true })` answered `downloadable`, and `install()` brought down
380 MB of es-ES and en-US packs). It hears only a microphone, so it was fed the
recordings two ways, and neither reached words:

- **A fake microphone** (`--use-file-for-fake-audio-capture`): silent with the
  shop's 16 kHz WAV, audible with a 48 kHz copy, and the recognizer still ended
  with `no-speech`.
- **The recording as a track** (`recognition.start(track)` over an
  `AudioContext`): `speechstart` and `speechend` fired, and no result, final or
  interim, ever did.

Two things in the way, for whoever tries again: Playwright launches Chrome with
`--disable-component-update`, under which Chrome forgets the language packs it
holds on every launch (they read `downloadable` again until the flag is
removed); and the packs live in the profile, so a throwaway profile downloads
them once. **The browser's engine is to be tried by hand, on a phone**, where the
shopper would use it; its share of words heard in the wild is what
`voice_input_heard` with `adapter: web-speech` will say.

## What is left to the author

- ~~The default model~~ **Ruled 2026-10-16: turbo q8_0** ("q8_0, me parece
  correcto tu razonamiento"). It hears what f16 hears, and q5_0's 286 MiB moved
  nothing on the shared card. The AppHost loads it, quantized by
  `src/AppHost/scripts/quantize-whisper.sh` and held to its hash. Parakeet stays
  configured beside it as the comparison and the CPU's answer.
- **The whole shop on one 8 GB card** is at its ceiling. It works, slower. The way
  out, if it matters, is placement, not quantization: speech on the CPU with
  Parakeet (3–3.7 s), or search's models on the CPU, for which there is no switch
  today (`Tendero:Inference:Device` moves every model at once).
- **Ruling 4 against what was measured.** As written, no GPU means no `shop`
  engine. The alternatives the table offers: Parakeet on the CPU at ~3.5 s, or a
  bar of four seconds instead of two.
- **es-15**: re-record it, or correct its line.

## Provenance

- Whisper large-v3-turbo `41f01f3f…` (MIT) and small `973afd24…` (Apache-2.0),
  converted by `src/AppHost/scripts/prepare-whisper.sh` with whisper.cpp's own
  converter; q5_0 and q8_0 by the server image's `whisper-quantize`. Hashes in
  the script.
- Parakeet-TDT-0.6B-v3 `541d1f99…` (CC-BY-4.0, accepted for tier 1 by the author;
  ADR 0061), exported by `src/AppHost/scripts/prepare-parakeet.py`.
- Silero VAD 6.2.0 (MIT), converted by the same script.
- The recordings: the author's phone recorder, 320 kbps MP3 at 48 kHz, redrawn by
  `tools/voice-eval/prepare.mjs` in Chromium with the storefront's own
  resampling. The per-line reports (`report-*.md`) are written to
  `artifacts/voice-eval/`, outside git, by `--report`.
