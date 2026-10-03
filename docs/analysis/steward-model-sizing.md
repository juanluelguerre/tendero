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

## Measured over the copilot suite (S-6, 2026-10-03)

The 24 cases of `tools/SearchEval --suite copilot`, each a real Steward turn —
the agent, the contexts' reads, proposals stored — against this container run
exactly as the AppHost runs it. Results by case are in `steward-models.md`.

| | |
|---|---|
| **Largest single call** (input + output) | **5,996 tokens** — 27 % under the 8,192 window |
| Tokens per turn (all its calls) | 4,195 on average |
| Turn latency | p50 22.5 s · p95 132 s · slowest 186 s |
| **Memory peak over the 24 turns** | **5.315 GiB** |

**The window stays 8,192 — now a measurement, not an estimate.** The heaviest
calls were the catalogue gaps' reads (images, drafts): fifty rows with names
handed back to the model as input. The read's size, not the answer's, is what
fills the window and what the local CPU spends its time on.

**The limit rises to 6 GB.** The single full-window request of S-2 peaked at
4.81 GiB; twenty-four turns in a row peaked at 5.315, and 5.5 GB left 0.19 GiB —
3 %. The peak plus 0.5 GB, rounded up to the next 0.5, is 6 GB.

**Two things the AppHost's arguments did not say, found by measuring:**

- **llama.cpp keeps a prompt cache in RAM, up to 8 GiB by default**
  (`--cache-ram 8192`). Under a 5.5 GB limit it grew turn after turn, and the
  kernel killed the container four cases in (`OOMKilled`, exit 137). S-2 measured
  one request, which never sees a cache grow. The AppHost now passes
  `--cache-ram 0`: with one slot, the slot itself already reuses the prefix every
  turn shares (the instructions and the tools — llama.cpp's log reports the
  reuse).
- **A window measured larger than it ships is not the same container.** A first
  attempt ran with a 12,288 window "so as not to cut anything short", under the
  same limit and without `--load-mode none`: the weights were mapped, not loaded,
  were evicted under the limit, and generation fell to **0.39 tokens/s**. Run
  exactly as shipped, it is **2.2 tokens/s**. That run's numbers were discarded.

The profile's timeout (480 s) and the turn's deadline (600 s) stand: the slowest
turn took 186 s.
