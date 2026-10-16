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
