# ADR 0061 — Speech is a port of AI, and the model behind it is configuration

**Status**: accepted (2026-10-16, phase 14)

## Context

Phase 14 lets a shopper speak a question into Tino's composer and into the
search box on a phone (`docs/specs/voice.md`). Two engines are compared under
one contract: the browser's Web Speech API, on the device only, and the shop's
own whisper.cpp server. The author accepted the spec with one addition: *use
interfaces in case we change the voice model*.

Two things had to be decided that no earlier ADR covers. The first is where the
transcriber lives on the server, when the search and Tino both use it. The second
is what "the model can change" means in code.

## Decision

- **`ISpeechToText` lives in `AI`**, beside the chat profiles. `AI` is the port
  every context reaches a model through (ADR 0053), and a transcriber is a model.
  No context owns a transcript: it is input, not state.
- **`AI` gains its first slice**, `Features/TranscribeSpeech`
  (`POST /api/speech/transcriptions`, `GET /api/speech/status`). The search box and
  Tino both call it, so it is neither `Search`'s nor `Assist`'s. Carter finds it as
  it finds every other module, and the host scans `AI` for handlers.
- **An engine is a keyed adapter, and the model is its options.** Each engine
  registers by its stable name (`whisper-cpp`, `scripted`), and `Speech:Engine`
  names the one the shop uses. Nothing outside an adapter names an engine.
  Nothing in code names a model: which Whisper weights whisper.cpp serves is its
  container's start-up argument, pinned in the AppHost. A new engine is one
  class and one registration, and it inherits `SpeechToTextContractTests` before
  it is used. A new model is a configuration change and a measurement.
- **The front end mirrors it**: the components depend on a `SpeechToText`
  interface, and which adapter answers is configuration.
- **What is listened to is fixed by the server**: a WAV of 16 kHz, mono, 16-bit
  PCM, from a quarter of a second to thirty seconds. Anything else is refused by
  its bytes with a code (ADR 0059), before an engine is asked anything. The
  browser encodes that format itself, so neither side needs a converter.

## Consequences

- **Without an engine, there is no microphone.** The status says so, a question
  sent anyway is a 503 with its code, and typing never passes through here
  (invariant 8).
- **The audio is never kept** (`docs/privacy.md`, *A question a shopper
  speaks*). The span carries the engine, the language, the length and whether
  anything was heard, never the words.
- `AI` now takes Carter, the package every context already uses: no new
  dependency for the solution.
- The OpenAPI document declares the body as `audio/wav` with a `byte` string
  schema. That is the framework's spelling for a byte array, and the front end
  sends a `Blob` either way.

## Amended 2026-10-16 — three engines, and a licence that is not OSI

- **The engines measured are three** (the author, the same day): Whisper
  large-v3-turbo on whisper.cpp in its own container, NVIDIA Parakeet-TDT-0.6B-v3
  **inside the process** on ONNX Runtime, and the browser's own on the device.
  No Python serves a model: no vLLM, so no Voxtral.
- **CC-BY-4.0 weights are accepted for tier 1**, by the author's ruling for
  Parakeet. The licensing policy names OSI licences (MIT, Apache-2.0, BSD); CC BY
  4.0 is not one, but it is permissive and allows commercial use, asking only for
  attribution and a note of what changed. Both are in `THIRD-PARTY-NOTICES.md`.
  This is a ruling for this model at this revision, not a widening of the policy
  for everything: the next CC-BY weights are asked about again.
- **Parakeet's features and its greedy TDT loop are C#**, and only its three
  networks are ONNX: `ParakeetFeatures` reproduces transformers 5.9.0's extractor
  and `TdtGreedy` its generation, behind an `ITdtNetworks` interface the loop is
  tested through with fakes. The fixture of two LibriSpeech utterances holds both
  to the maker's numbers.
- **whisper.cpp listens through Silero VAD.** Without it, silence was heard as
  "Gracias.", and its no-speech probability cannot be trusted with turbo. The
  contract every engine inherits now says, with proof, that silence and a tone are
  nothing heard.
