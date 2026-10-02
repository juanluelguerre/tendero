# Steward's local model — sizing (S-2, 2026-10-02)

The `steward-model` container serves the claim extractor's GGUF (Qwen3.5-4B Q8,
ADR 0031) from the extractor's volume, with a slot of its own: one sequence,
a window of 8,192 tokens, weights read into memory (`--load-mode none`) so the
limit is honest, thinking off. These are the numbers its limit is set from.

## How it was measured

- llama.cpp `server-v0.5.0`, the same arguments the AppHost passes, alone in a
  container limited to 10 GB and 12 CPUs, on the author's machine (16 CPUs,
  31 GB for Docker) with the development stack also running.
- One chat request built to fill the window the way a Steward turn would: a
  system prompt (~700 tokens, repeated), the six read tools' definitions, and a
  long user message of catalogue text — **7,996 prompt tokens**, `max_tokens`
  800, temperature 0.
- `docker stats` sampled continuously during the request (129 samples).

## What it showed

| | |
|---|---|
| Memory after loading, idle | 4.58 GiB |
| **Peak during the request** | **4.81 GiB** |
| Prompt tokens / generated tokens | 7,996 / 181 |
| Prompt processing | 43.1 tokens/s |
| Generation | 2.3 tokens/s |
| Wall time for the request | **263 s** |

**The limit is 5.5 GB** — the peak plus 0.5 GB, rounded up to the next 0.5 GB,
the rule every container here follows.

## What it means for Steward

- **A turn with a full window takes about four minutes on this CPU**, and with
  this request's short answer (181 tokens) prompt processing was most of it
  (~185 s for 8k tokens). But generation is the slow half for an ordinary
  answer: the spec's typical turn — a prompt of up to 4,700 tokens and an answer
  of 400–800 — is ~110 s of reading plus 174–348 s of writing, **up to ~458 s**.
  The local profile's timeout is therefore **480 s** (first written as 180, then
  300 from this request alone; the final review did the arithmetic). The lever
  that matters is the answer's length: S-3 caps Steward's output tokens, and S-6
  re-derives the timeout from the largest real turn.
- Typical turns are smaller (the spec's estimate is 2,700–4,700 tokens), which
  this rate puts at roughly 60–110 s of prompt processing before the first
  generated token. That is the number S-6's evaluation and S-7's comparison with
  hosted profiles start from, and the reason the panel shows progress rather
  than streaming (a spec non-goal revisited after S-7).
- The development stack was running beside it; on an idle machine the rates
  will be somewhat higher. With `Tendero__Knowledge` on as well, the extractor
  and `steward-model` each take 12 threads of the machine's 16: when both are
  busy they share the CPU, and these figures — measured with `steward-model`
  alone — are the best case. The window stays an estimate until S-6 records the
  largest real conversation.

## The contract run (2026-10-02)

`tests/AI.Tests`, `Category=Model`, against `steward-model` with the 5.5 GB
limit: **three of three pass** — the 4B calls a tool through the default chat
template and answers with its result, reports input and output tokens, and a
timeout surfaces as one.

The first run failed the third, and that was the finding: the OpenAI SDK
retries a failed call three times by default and then throws an
`AggregateException`. With the local profile's 300 s timeout, a stuck turn would
have taken twenty minutes to fail and been metered as one call. The client now
makes exactly one request per call (`ClientRetryPolicy(maxRetries: 0)`, a unit
test holds it); trying again, or the fallback, is the caller's decision — S-3's.
