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

- **A turn with a full window takes about four minutes on this CPU**, and
  prompt processing is most of it (~185 s for 8k tokens). The local profile's
  timeout is set to 300 s, not the 180 s first written in the plan.
- Typical turns are smaller (the spec's estimate is 2,700–4,700 tokens), which
  this rate puts at roughly 60–110 s of prompt processing before the first
  generated token. That is the number S-6's evaluation and S-7's comparison with
  hosted profiles start from, and the reason the panel shows progress rather
  than streaming (a spec non-goal revisited after S-7).
- The development stack was running beside it; on an idle machine the rates
  will be somewhat higher. The window stays an estimate until S-6 records the
  largest real conversation.

## The contract run

Recorded below after Task 7.
