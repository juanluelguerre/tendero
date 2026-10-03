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
| hosted (S-7) | | | | | | | | | |
| small model (S-7) | | | | | | | | | |

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
