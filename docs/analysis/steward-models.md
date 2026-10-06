# Steward's models — measured with the copilot suite

The spec's decision 1: the models are profiles behind one port, measured before
one is chosen, and the measurement comes last. This file holds the measurements;
the author picks Steward's default with them. **S-6 measured the local profile;
S-7 adds the hosted ones and the small model** with the same suite and the same
laboratory, so the rows compare.

## How

`tools/SearchEval --suite copilot` (its README): 24 cases in es and en (26 from
2026-10-05, with two on the answer's language) over the
seed catalogue (100 products), in a disposable Postgres the suite migrates and
seeds; each case a fresh conversation through the turn the API runs. A case
passes when the model made the reads it should, with the arguments that matter,
and stored exactly the proposals it should — compared, no judge model. A dry run
with a scripted model scores every case, which proves the suite before a model is
measured with it.

## Results

| Profile | Passed | Right tools | Valid arguments | Exact proposals | p50 | p95 | Tokens/turn | Largest call | €/turn |
|---|---|---|---|---|---|---|---|---|---|
| **local** — Qwen3.5-4B Q8, llama.cpp `server-v0.5.0`, 12 CPUs, 6 GB, 8,192 window | **22/24 (92 %)** | 100 % | 96 % | 96 % | 22.5 s | 132 s | 4,195 | 5,996 | 0 |
| **local on the GPU** — the same Qwen3.5-4B Q8, llama.cpp CUDA 12 image, every layer offloaded, RTX 5070 Laptop (8 GB) | **22/24 (92 %)** | 96 % | 92 % | 100 % | **1.9 s** | 8.9 s | 4,268 | 6,117 | 0 |
| **local on the GPU, prompt `steward-2026-10-05`** (P12-10) — same model, image and card; two runs | **23/24 and 24/24** | 100 % · 100 % | 96 % · 100 % | 100 % · 100 % | 2.2 s · 2.2 s | 7.1 s · 7.4 s | 4,766 · 4,904 | 5,926 · 6,056 | 0 |
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

## The prompt of P12-10, measured (2026-10-05)

The prompt changed (`steward-2026-10-05`: say only what a tool returned, do not copy
the rows, call the `propose_` tool, no picture descriptions, answer in the language
of the last message, the panel draws Markdown) and the suite was run twice on the same
model, image and card as the row above it: **23 of 24, then 24 of 24**, against 22 of
24 before. Two runs are not a distribution — the 4B varies between runs, and the one
miss (`restock-barcelona`: it sent `Barcelona`, was told the warehouses, and asked
the shopkeeper instead of asking again) passed on the second. What can be said is that
the new prompt cost nothing the suite measures; tokens a turn rose from 4,268 to about
4,800, which is the longer instructions.

What the suite does not score, read from the 48 answers:

- **Language**: every answer was in the language of its question. The suite sends each
  case with its own culture, so the case that failed for the author — a Spanish
  question under an English culture — is **still unmeasured**; it needs a case of its
  own.
- **A tool call written as text**: none in 48 answers, so the guard was not exercised
  by a real model here (its test is the scripted one).
- **Not copying the rows is followed in part.** Short where a read returned nothing or
  a figure; but `gaps-alt-text` still lists its 18 products, and `gaps-drafts` and
  `restock-barcelona` write long lists (one a Markdown table) that the panel now draws
  as a table as well. The instruction does not hold on a 4B; if the duplication
  matters, code has to enforce it, not the prompt.
- **`find-cheapest-lamp` passes on its tools and answers badly**: it narrates its plan
  ("I need to search…"), reads `sales_summary` looking for prices, and no read carries
  a price. The gap is a missing read, not the model.
- **The picture rule holds**: asked what lacks alt text, it listed the products and
  said it cannot see or describe pictures.

## Three things that followed, measured the same day

The reading above led to three changes, and the suite was run again — **each run on a
fresh database**, five in all, on the same model and card.

| | Passed (of 26) |
|---|---|
| With all three changes, two runs | 23 · 25 |
| The same without the price on `find_products`, three runs | 25 · 23 · 25 |

1. **The suite scores the answer's language where a case asks for it.** Two cases:
   a Spanish question under an English culture — the author's — and its mirror. The
   language is told by the commonest words of the answer's opening, no model
   (`CopilotScoring.LanguageOf`). **Spanish under English: 7 of 7.** English under
   Spanish: 5 of 7 — a Spanish culture still pulls a 4B back to Spanish two times in
   seven, which the prompt does not cure.
2. **Rows the model copies are taken out by code** (`CopiedRows`): a list item or a
   Markdown table row that names a row of a table the panel draws goes, once there are
   more than three; the line that only announced them goes too, and one that states a
   figure stays as a sentence. On the real answers `gaps-alt-text` fell from about
   1,100 characters to about 200, and the count is still said.
3. **`find_products` carries the price.** `find-cheapest-lamp` now answers the
   question — the right lamp at its price, twice of two — and no longer reads
   `sales_summary` for something it does not hold. It costs nothing the suite measures:
   the two rows above are the same spread.

**What five runs say about the earlier two.** 23 to 25 of 26 is the spread, and the
24 of 24 above was the lucky end of it. Two cases fail by turns and neither is new:
`restock-barcelona` (told the warehouses, it asks the shopkeeper instead of asking
again) in four runs of five, and `translate-beanie-spanish` (the product already has
a Spanish name, so it declines to propose another) in three of five. Both are the 4B
not acting; code cannot propose for it, and a hosted profile is S-7's question.

**A trap in the method.** Two runs made on the database a dry run had just used scored
23 and 23 with failures of a different kind (a proposal for the wrong product, a search
for the new name). They are not counted above. A database per run, or the comparison
means nothing.

### The screen's language (2026-10-06): the 4B does not follow it from Spanish

The author changed the rule for both agents: an answer is in the language of the
screen, not of the last message (prompt `steward-2026-10-06`; the line about
switching language on request went with it — the backoffice has a switch). The
suite's two language cases flipped with it: `spanish-under-english` expects
English, `english-under-spanish` Spanish.

**Measured the same day, local 4B on the GPU, four runs:**

| Case | Passed | What happens |
|---|---|---|
| `english-under-spanish` | 4 of 4 | an English question on the Spanish screen is answered in Spanish |
| `spanish-under-english` | **0 of 4** | a Spanish question on the English screen is answered in Spanish: *"Las ventas de la última semana han sido de 0 €…"* |

Steward's answer is the model's prose, so here the rule is only a line of the
instructions — unlike Tino, where code sets the language of the reads and writes
its own sentences. Two of the four runs had a second line, last in the prompt
(*Write your answer in English only.*, `…06b`): **it changed nothing, and was taken
out again.** A Spanish question pulls the 4B to Spanish whatever it is told.
**Ruled by the author the same day: both agents answer in the language chosen in
the menu.** So code holds it (prompt `steward-2026-10-06b`): an answer plainly in
another language than the screen's — told by its function words, no model
(`WrittenLanguage`, in the kernel, the count Knowledge already held a claim's
statement to) — is handed back to the same profile to be said again, with no tools
and nothing else to read. What comes back is shown only if it is in the screen's
language **and states the same figures, no more and no fewer**; otherwise the
answer stands as first written. One more call, only on the turns that need it.

| Run (a database each) | `spanish-under-english` | The answer | Whole suite |
|---|---|---|---|
| 5 | pass, 2.9 s | *Last week's sales were 0 € in revenue, 0 orders, 0 units…* | 24 of 26 |
| 6 | **answered in English**; scored *no language it could be told* | *\*\*Last week's data (no prior comparison):\*\** and a list | 23 of 26 |
| 7 | pass, 4.1 s | *Sales data for the last week shows zero across all metrics…* | 24 of 26 |

Three of three in English, against none of four. Run 6 is the judge's miss, not
Steward's: `CopilotScoring.LanguageOf` reads the answer's opening paragraph, which
there is a heading with no function word in it. The case costs one to two seconds
more than its twin, the second call. `english-under-spanish` still passes without
it, three of three.

**Whole suite: 24 of 26 on the first run** (`translate-beanie-spanish`, a known
one, and the language case). **The other three runs do not count for anything but
the two language cases**: they reused the first run's database, against this
file's own rule above, and failed 2 to 4 cases of the kind that leaves
(`withdraw-padel`, `translate-this-one`, `restock-barcelona`). The language cases
only read, so their eight results stand.
