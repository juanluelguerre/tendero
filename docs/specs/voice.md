# Spec: Voice — speaking to Tino and to the search box

**Status**: accepted (2026-10-16), the seven rulings as proposed; building
**Phase**: 14 · **Branch**: `feature/phase-14-voice` (to be cut from `develop`)
**Slice(s)**: storefront (`tino/`, the search box), a speech port in the front end, `AI` or a slice of its own for the shop's transcriber, the AppHost (a whisper.cpp container behind a switch)
**Article**: *"Talking to the shop"* (working title)

## Problem

A shopper on a phone types badly and asks well. *Quiero unas zapatillas para
correr por montaña que no pesen mucho* is long to type and easy to say, and it is
exactly the kind of question hybrid search and Tino answer best. Today the only
way in is a text box. This was `P12b-5` and `P12b-6`, postponed by the author on
2026-10-05 and 2026-10-06 to a phase of its own, after the images.

The decision this phase has to make with numbers is **who hears the shopper**.
The browser's Web Speech API needs no server, but in Chrome the audio goes to
Google unless the browser can recognise on the device. A whisper.cpp server is
the shop's own and the audio stays with the shop, at the cost of a container and
a model. Tendero does not choose by taste: both are adapters of one port, held to
one contract suite, measured on the same recordings, and the author picks the
default with the numbers in front of them.

## Behavior

### V-1 · One port, two adapters

- **The port lives in the front end** (`SpeechToText` in `libs/shared/util` or a
  storefront theme folder): *listen, then give me the words in this language*. It
  answers a transcript, or *nothing heard*, or *not available*; a failure is never
  a half-written question.
- **`web-speech`**: the browser's `SpeechRecognition`. Asked to recognise on the
  device (`processLocally`) where the browser can, and saying plainly when it
  cannot, because then the audio leaves for the browser maker's servers.
  Feature-detected: without it, this adapter is *not available*.
- **`shop`**: the microphone's audio is posted to the shop
  (`POST /api/speech/transcriptions`, anonymous like the search, rate-limited),
  and the API asks a whisper.cpp server through a .NET port (`ISpeechToText`,
  keyed adapters, ADR-0053-style). The audio is never kept: it is transcribed and
  dropped, and `docs/privacy.md` says so.
- **One contract suite** (front end: both adapters against recorded audio
  replayed as a stream; back end: `ISpeechToText` against a fake and the real
  server, the real one skipped with a reason when it is not running).
- **The model is swappable, by the author's ruling** (2026-10-16: *use
  interfaces in case we change the voice model*). Nothing outside an adapter
  names an engine or a model:
  - the front end depends on the `SpeechToText` interface, and which adapter
    answers is configuration;
  - the API depends on `ISpeechToText`, whose adapters register as keyed
    services (invariant 3); a new engine (another Whisper build, a hosted
    service, a future model) is one class and one registration, and inherits the
    contract suite before it is used;
  - the model an engine loads (weights, revision, quantization, language hints)
    is the adapter's options, never code, so trying another model is a
    configuration change and a measurement, not a rewrite;
  - the measurement of V-5 is a tool over the interface, so a new adapter is
    measured the same way the first two were.

### V-2 · The transcript lands in the box, unsent

What was heard is written into the text box, and **nothing is sent**: the
shopper reads it, fixes it if they want, and presses send. A model never sends a
question the person did not see (invariant 12, applied to input). The language is
the page's (as Tino's answers are), so a Spanish page listens for Spanish.

### V-3 · Where the microphone is

- **Tino's composer**, beside the send arrow, on every width.
- **The search box on a phone**, beside the camera of visual search. Not on a
  desktop: a keyboard is there already.
- Neutral, never clay (the page's one accent stays *Añadir al carrito*); a size
  from the tokens (`min-h-tap`), a label in es and en for a screen reader, and a
  visible *listening* state that does not rely on colour alone. Checked with the
  keyboard and a screen reader, not assumed.

### V-4 · The degraded path (invariant 8)

With no adapter available — no `SpeechRecognition`, the switch off, the server
down, the permission refused — **there is no microphone**, and the text box is
unchanged. A failure while listening says so in one line beside the box, in the
page's language, and leaves what the shopper had typed alone. Tested, not
asserted: a browser spec with the API removed, and one with the server down.

### V-5 · The measurement and the default

The same recordings through both adapters, per language: **word error rate**,
**time from the end of speech to the words**, and for `shop` its **memory, CPU
and GPU** under one request at a time. The result is a table in
`docs/analysis/voice-models.md`, and the author chooses the default adapter from
it (`P12b-5`'s own words). The non-default one stays, configured, as the
comparison an article can show.

## Non-goals

- **Talking back.** Tino's answers stay text: text to speech is another phase.
- **A voice shop** (smart speakers, a phone line). Voice through assistants that
  act as agents is what grows (the analysis of 2026-09-26), and that is MCP's and
  UCP's door, not this one.
- **Keeping audio**, for training or anything else.
- **Streaming partial words into the box** while the shopper speaks. The first
  version writes the transcript when they stop; partial results are a later step
  if the measurement says waiting feels long.

## Measures

- The contract suites above, front and back.
- Browser specs: the microphone appears only when an adapter is available; a
  transcript lands unsent in Tino's box and in the phone search box; with the API
  removed and with the server down, the box is as before.
- `docs/analysis/voice-models.md`: WER and latency per adapter, language and
  model size, with the recordings' provenance.
- Stryker at the phase close over every project touched, as usual.

## Evidence of use

| Event | The question it answers | The decision its value would change |
|---|---|---|
| `voice_input_heard` (where: tino, search; adapter; outcome: words, nothing, failed, cancelled) | Does anyone speak to the shop, and does it understand them? | Almost nobody, after a month of traffic: the microphone leaves the search box and stays only in Tino. Most questions not heard: the engine or its model is wrong for this shop |
| `voice_transcript_used` (sent as heard, edited, discarded; adapter) | Is what was heard good enough to send? | A high share edited or discarded for one adapter is the measurement repeated in the wild, and moves the default |

Neither carries the words or the audio, and both follow the storefront's
consent. The first was proposed as `voice_input_started`; it is sent when
listening ends instead, because only then is there an outcome to count, and a
start without one answers half the question.

## The author's rulings (2026-10-16)

All seven as proposed ("la spec me parece correcta"), with one addition: every
engine and model behind an interface, so the voice model can change (V-1).

| # | Question | Ruled |
|---|---|---|
| 1 | Both adapters, compared, as `P12b-5` says? | **Yes**: the point of the phase is the comparison |
| 2 | Which Whisper weights for `shop`: `large-v3-turbo` (809 M parameters, about 1.6 GB in f16, less quantized) or a smaller one (`small`, `base`)? | Measure **turbo quantized** and **small** on the same recordings; pin the revision and convert ourselves (ADR 0031), weights MIT, checked at the exact revision when added (tier 1) |
| 3 | The recordings the measurement uses: who records them? | **The author records** about twenty short questions per language, from the golden sets' own queries and Tino's suite, read naturally once each; kept in the repository with their transcript. Synthetic speech would flatter both adapters |
| 4 | `shop` on a machine with no GPU: off, or on the CPU? | **On the CPU** if turbo quantized answers a short question in under two seconds there (measured first); off otherwise, and saying so, as Tino does |
| 5 | Web Speech in Chrome when on-device recognition is not available: offer it anyway, saying the audio goes to Google, or not at all? | **Not at all by default**: only `processLocally`. Whether the shop ever sends a shopper's voice to a third party is the author's call, and it would go in `docs/privacy.md` |
| 6 | The phone search box too, or Tino only first? | **Both**, the search box only on a phone |
| 7 | The whisper.cpp server image | The project's own release image at an exact tag, the same way llama.cpp is pinned (tier 2, MIT), sized from a measurement (`--memory`, `--cpus`) |

### The models, ruled again the same day

Asked why those two models and not others, the author had the open weights
compared, and ruled:

- **Parakeet-TDT-0.6B-v3 joins the measurement**, on ONNX: "me gusta por usar ONNX
  para ello". Its licence at revision `541d1f99` is **CC-BY-4.0**, which is
  permissive but not an OSI licence; **accepted for tier 1 by the author**, with
  the attribution in `THIRD-PARTY-NOTICES.md` (ADR 0061, amended).
- **Option A: inside the .NET process**, on ONNX Runtime, as SigLIP 2 is: our own
  export of its three networks (`scripts/prepare-parakeet.py`, Python only to
  export), and the mel features and the greedy TDT loop in C#
  (`AI/Speech/Parakeet`), held to the maker's numbers.
- **No Python container to serve a model** (vLLM), so **no Voxtral**.
- **`small` is dropped**: Parakeet takes the place of the small and fast engine.

- **The Whisper model is large-v3-turbo in q8_0** (ruled after the measurement,
  `docs/analysis/voice-models.md`): the same words as f16 on all forty
  recordings in 64 % of the card's memory. q5_0 was measured beside it and saved
  nothing measurable on a card the rest of the shop shares.

So the measurement (V-5) compares **Whisper large-v3-turbo** (whisper.cpp),
**Parakeet-TDT-0.6B-v3** (ONNX, in process) and **`web-speech`** (the browser, on
the device), on the author's recordings.

**Found while wiring whisper.cpp**: on silence and on a tone, Whisper writes
words never said ("Gracias.", "Thank you.", "¿Qué?"). The contract suite caught
it, and its no-speech probability says 1.7·10⁻⁹ about pure silence. The server
therefore runs with **Silero VAD** in front (MIT, converted by us), and listens
only where it hears speech.
