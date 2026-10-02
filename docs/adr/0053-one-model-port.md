# 0053 — One model port: profiles in configuration, one pipeline, one row per call

**Status**: accepted (the author approved S-2's plan on 2026-10-02, which proposed it)

## Context

Until phase 12 the only generative model was the claim extractor, and the only
factory for it lived in Knowledge (`Knowledge.Chat`). Steward needs the same
factory, so will Tino (phase 12b), and Steward's spec asks for providers that are
entries in configuration rather than classes — the local model first, hosted
ones (Gemini, DeepSeek) measured later — with the cost and latency of every call
recorded, because hosted tokens are paid.

Steward lives in Assist, which may reference only the SharedKernel; the kernel
must not carry the OpenAI SDK or ONNX Runtime; and Knowledge is a context, so it
cannot be the shared place.

## Decision

1. **`ElGuerre.Tendero.AI`** is a project of its own, not a context (it owns no
   aggregate, like `Mcp` and `Ucp`). It references the SharedKernel and nothing
   else, and an architecture rule computes that. The acronym is in capitals, as
   .NET writes two-letter acronyms (`Microsoft.Extensions.AI`, `AIFunction`).
2. **The SDKs stay in `AI.Chat`.** The OpenAI SDK may be used there, in Search's
   embeddings and in Knowledge's support scorer (which reads raw log
   probabilities) and nowhere else; ONNX Runtime only in `AI.Chat`. Rules hold
   both.
3. **A profile is configuration** (`Steward:Models:<name>`: kind, endpoint with
   `/v1`, model, auth, key, prices in € per million tokens, window, timeout),
   registered as a keyed `IChatClient`. A profile that cannot be called stops
   the host at start, naming itself.
4. **One pipeline for every profile and for the extractor**: OpenTelemetry (the
   GenAI conventions) → a metering client → the model. Function invocation is
   added outside it by the caller (S-3).
5. **One row per model call** in `telemetry.ai_invocations`: operation,
   profile, model, prompt version, conversation, trace, tokens, cost in
   micro-euros at the profile's price of that moment, latency, outcome. The
   operation, conversation and prompt version travel in the call's
   `ChatOptions`, set where the call is made.
6. **Metering never holds the request**: rows go to a bounded in-process channel
   that refuses when full (counted as `tendero.ai.invocations.dropped`), and a
   worker writes them in batches on a context of its own; a batch that cannot be
   written is logged and dropped, the audit's trade.
7. **One request per call.** The OpenAI SDK's three default retries are off:
   with a 300 s local timeout they turned a stuck call into twenty minutes,
   metered as one. Trying again, or the fallback, is the caller's decision.
   That holds for the claim extractor too: a transient 5xx or 429 now costs one
   of a product's attempts and its one-minute pause, where the SDK used to retry
   inside the attempt — and a stuck call no longer takes forty minutes.
8. **A profile is registered where it can be reached.** One with no endpoint is
   left out without complaint — the local profile until the AppHost hands it an
   address — and every other profile is a client regardless; one that is half
   configured stops the host.
9. **Steward's local model is a container of its own** (`steward-model`) serving
   the extractor's GGUF from the extractor's volume with an 8,192-token slot,
   limited to 5.5 GB from a measurement (`docs/analysis/steward-model-sizing.md`).
   The API is handed its address only with `Tendero__Steward=true`. Its timeout
   is 480 s: the typical turn takes up to ~458 s on the CPU, most of it writing
   the answer.

## Consequences

- A new provider is a configuration entry; a new kind of provider is a case in
  the factory and a class that passes the profile contract suite.
- The extractor's calls are metered too, under `knowledge.extract`.
- A full log loses rows, and says how many; a dashboard built on the table is
  exact only while that counter stays at zero.
- A full-window turn on the local model takes about four minutes on the
  author's CPU — the number S-6 and S-7 start from.
