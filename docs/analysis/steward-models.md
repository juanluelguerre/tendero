# Steward's models — measured with the copilot suite

The spec's decision 1: the models are profiles behind one port, measured before
one is chosen, and the measurement comes last. This file holds the measurements;
the author picks Steward's default with them. **S-6 measured the local profile;
S-7 adds the hosted ones and the small model** with the same suite and the same
laboratory, so the rows compare.

## How

`tools/SearchEval --suite copilot` (its README): 24 cases in es and en over the
seed catalogue (100 products), in a disposable Postgres the suite migrates and
seeds; each case a fresh conversation through the turn the API runs. A case
passes when the model made the reads it should, with the arguments that matter,
and stored exactly the proposals it should — compared, no judge model. A dry run
with a scripted model scores 24/24, which proves the suite before a model is
measured with it.

## Results

| Profile | Passed | Right tools | Valid arguments | Exact proposals | p50 | p95 | Tokens/turn | Largest call | €/turn |
|---|---|---|---|---|---|---|---|---|---|
| **local** — Qwen3.5-4B Q8, llama.cpp `server-v0.5.0`, 12 CPUs, 6 GB, 8,192 window | **22/24 (92 %)** | 100 % | 96 % | 96 % | 22.5 s | 132 s | 4,195 | 5,996 | 0 |
| **local on the GPU** — the same Qwen3.5-4B Q8, llama.cpp CUDA 12 image, every layer offloaded, RTX 5070 Laptop (8 GB) | **22/24 (92 %)** | 96 % | 92 % | 100 % | **1.9 s** | 8.9 s | 4,268 | 6,117 | 0 |
| hosted (S-7) | | | | | | | | | |
| small — Phi-4-mini Q8 (MIT), same llama.cpp and settings | **1/24 (4 %)**, not a verdict on the model: **its tools never reached it** | 8 % | 8 % | 75 % (only "never approves" passes) | 4.0 s | 8.7 s | 311 | 375 | 0 |
| small — Qwen3-1.7B Q8 (Apache-2.0), same llama.cpp and settings | **9/24 (38 %)** | 46 % | 42 % | 75 % | 8.9 s | 15.9 s | 2,633 | 3,444 | 0 |

Measured on 2026-10-03, on the author's machine (16 CPUs), the development
containers idle beside it.

## The local profile's two failures — both a model asking instead of acting

- **restock-barcelona** — asked what to restock *in Barcelona*, it passed
  `{"warehouse":"Barcelona"}`. The read refused it and named the warehouses'
  codes; the model understood (*"the warehouses are BCN (Barcelona) and MAD"*)
  but asked *"Do you want me to tell you what to restock in BCN?"* instead of
  calling again with `BCN`.
- **translate-beanie-spanish** — asked to propose *"Gorro de punto de merino"* as
  the Spanish name, it found the product already has one (*"Gorro de punto de
  lana merino"*) and asked whether to propose a different one, instead of
  proposing what it was asked to.

Both are prudent and neither is what was asked. They are the prompt's to
answer — *when a read tells you the right value, use it and call again; when the
shopkeeper asks you to propose, propose* — and a prompt change is versioned and
re-measured, so it waits for S-7's comparison rather than moving the baseline
the hosted profiles are measured against.

## What the suite found before it measured anything

Three bugs, all in the shipped Steward, none visible to a test before: the tool
schemas the OpenAI client and llama.cpp refused (every turn against a real model
failed), and the container's prompt cache that would have had the kernel kill it
a few turns in. `steward-model-sizing.md` and the notebook tell both.

## Phi-4-mini: not measurable as served (2026-10-03)

`bartowski/microsoft_Phi-4-mini-instruct-GGUF` Q8_0 (4.08 GB; base model MIT),
under the same llama.cpp image and flags as the local profile. One case of 24
passes, and the reason is not the model: a direct request with one tool had a
prompt of **9 tokens** — the tool never reached the model, which answered that it
had no access to sales data. The template embedded in the GGUF renders tools only
from a `tools` key *inside the system message* (`'<|tool|>' + message['tools']`);
llama.cpp, given the OpenAI-style `tools` list, hands them to the template as a
variable the template never reads. Every turn is therefore ~300 tokens, against
the 1,700 of the Qwen run, and the 4 s median is the speed of a model that was
asked nothing.

Using it would mean our own chat template, and a parser for its `<|tool_call|>`
output, which llama.cpp does not know for this template: work on the serving,
not a measurement. Not done. The next small model to try is one whose template
llama.cpp already speaks, such as Qwen3-1.7B.

## Qwen3-1.7B: fast, and not enough (2026-10-03)

`Qwen/Qwen3-1.7B-GGUF` Q8_0 (1.83 GB, Apache-2.0), same image and flags as the
local profile. Its template speaks llama.cpp's tool format, so the tools reach it
(a direct request: 155 prompt tokens and a valid `sales_summary` call), unlike
Phi-4-mini. Nine cases pass, **two and a half times faster** than the 4B (p50
8.9 s against 22.5 s, the slowest turn 49 s):

- **Passes**: the five catalogue-gap reads, `sales-week`, `best-sellers-month`,
  `returns-week` and `never-approves`.
- **Fails by not calling the tool**: 13 of 15 failures end *"never called X"* —
  it answers from nothing instead of reading, on the same questions the 4B reads
  for (`sales-month` fails where `sales-week` passes). It never finds a product
  before proposing, so none of the translate and withdraw cases stores anything.
- **Fails by a wrong argument**: `restock-barcelona` passes `Barcelona`, as the 4B
  did, and the read's refusal naming `BCN` was not enough.

A model that proposes changes to the catalogue on 38 % of its questions is not a
fallback worth having; below the 4B, the speed is bought with the thing Steward is
for. The suite's three rows now say where the line is: **4B local 92 %, 1.7B 38 %,
Phi-4-mini not servable**. The hosted profiles are still to measure.

## The same model on the GPU (2026-10-03)

The 4B is the only local model that does the job (92 %; the 1.7B 38 %, Phi-4-mini
cannot be served), and what was wrong with it was time. On the author's RTX 5070
Laptop it passes the same 22 of 24 — a different two (`find-cheapest-lamp` and
`restock-barcelona`: the model varies a little between runs) — with a **median
turn of 1.9 s against 22.5 s, the slowest 12 s against 186**: twelve times
faster at the median and fifteen at the worst. It holds 4.8 GB of the card's 8 GB
(the embeddings take 1.3 GB beside it) and 1.1 GiB of RAM instead of 5.3.

`server-cuda-v0.5.0` works with the machine's driver (610.88); `server-cuda13-v0.5.0`
refuses to start: *requirement error: unsatisfied condition: cuda>=13.4*. The
AppHost uses the GPU when the embeddings do (`Tendero__Inference__Device`).
