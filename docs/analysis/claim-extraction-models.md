# What reads a description: choosing the claim extractor

Phase 10, before `P10-3`. Opened 2026-09-26, when the author asked *what do we
need a model for?* and then *how does llama.cpp work, and is there anything
better — a newer model, ONNX, or the new JEPA models?* This document answers
both — and then *wouldn't a decision model, Jev, fit this?* — keeps every decision with its date and its reason **at the moment it was
taken**, and declares what the spike must measure before it measures it — the
same order as [`otel-vs-faro.md`](otel-vs-faro.md).

Feeds chapter 30 (*The AI writes claims*) and chapter 26 (*Model weights are
dependencies*), and the phase 10 article.

## 1. The job

`ProposeClaimsFromDescription` (roadmap B4) reads a product's **existing**
description and proposes **claims**: each one a statement in es and en, a typed
value when it maps to an attribute the catalogue defines, a confidence, and the
**exact quote** it rests on. A person approves or rejects it; only then does the
catalogue change (invariant 12).

What that asks of the extractor, from the seed catalogue's hundred descriptions:

| kind of claim | example (the description's own words) | what finds it |
|---|---|---|
| a number with a unit, an attribute we define | *"Drop de 4 mm"* → `DROP_MM = 4` | a rule |
| a number with a unit, no attribute | *"Cortavientos de 140 g"*, *"tiro de 13 cm"* | a rule finds it; nothing to map it to |
| a boolean attribute, in words | *"apta para inducción, horno y fuego directo"* → `INDUCTION = true`, plus two claims | a model |
| a property, in words | *"se seca en una hora"*, *"asa fría"*, *"cabe en su propio bolsillo"*, *"cierre que no abre bajo presión"* | a model |

**44 of the 100 descriptions contain a digit at all** (counted 2026-09-26), and
not every digit is a spec. The rest of what a shopper asks — and what
`CompareProducts` must align — is in words.

**What the code keeps for itself**, whatever reads the text (invariant 12 —
*the model parses and phrases, code chooses*):

- the quote must occur **verbatim** in the description, or the claim is dropped
  and counted — a model cannot cite a sentence that is not there;
- a typed value must parse against the `AttributeDefinition` it names (kind,
  unit, option codes), or it stays a statement without a value;
- the statement in the other language is checked to exist; the description is
  already bilingual, so the quote in each language is looked up the same way.

## 2. How llama.cpp works

For the reader who has used a hosted model and never run one.

- **A model is a file of numbers.** The weights published on Hugging Face are
  usually `safetensors` in 16-bit floats: a 4-billion-parameter model is about
  8 GB.
- **GGUF is llama.cpp's file format**: the weights, the tokenizer and the chat
  template in one file, usually **quantized** — each weight stored in about 4
  or 8 bits instead of 16. A 4B model at 4 bits is ~2.5 GB and runs on a laptop
  CPU. Quality drops a little; the spike measures how much for this job.
- **`llama-server`** loads one GGUF and serves it over HTTP, including an
  **OpenAI-compatible** `/v1/chat/completions`. That is why `IChatClient`
  (Microsoft.Extensions.AI) can talk to it through the OpenAI adapter with no
  llama.cpp-specific code — and why the same adapter would reach vLLM or a
  hosted model later.
- **Generation is one token at a time.** At each step the model scores every
  token in its vocabulary; the server picks one; repeat.
- **Constrained decoding** is what makes it usable here. Given a JSON Schema
  (`response_format: json_schema`), the server converts it into a grammar
  (GBNF) and, **at every step, forbids every token the grammar does not allow
  next**. The output is valid JSON of our shape by construction, not by
  asking nicely in a prompt.
- **Deterministic enough**: temperature 0 and a fixed seed give the same answer
  for the same input on the same build and hardware — which is what lets the
  claims eval (`P10-9`) be a regression gate rather than a mood.
- **One process per model**, a container beside the app (Tier 2: llama.cpp is
  MIT). The model file is the Tier-1 dependency, checked under ADR 0031.

## 3. The options

| | what it is | for this job | against |
|---|---|---|---|
| **A · Rules only** | patterns over the text, in C# | deterministic, no model, the non-AI path invariant 8 requires anyway | finds numbers, not words; and what code decides needs no approval, so the review queue would have nothing to review |
| **B · An extractive encoder** — GLiNER2 | a ~300M-parameter model that returns **spans of the input** with a score, from a schema of labels | its output *is* a quote with a confidence; multilingual variant (mDeBERTa); Apache-2.0; an ONNX export exists | its runtime is Python; in .NET we would own tokenisation and span decoding; it extracts and classifies but does not **write** the statement in the other language |
| **C · A small generative model, llama.cpp** | a 2–9B instruction model, GGUF, `llama-server` | reads words, maps them to our attributes, writes both languages, JSON by grammar | a container and ~2.5–6 GB of weights; seconds per product on CPU |
| **D · The same, in process** — ONNX Runtime GenAI | Microsoft's generative runtime for ONNX, with a C# API and JSON-schema constraints through LLGuidance | no container | the model loads inside the worker's process and memory; its constrained decoding is newer and partly build-from-source |
| **E · The same, Ollama** | a friendlier wrapper over llama.cpp | simplest to run | its library models name no source revision — the reason ADR 0031 rejected it for `bge-m3` |
| **G · A decision model** — Jev (TypeSafe AI, 2026-09-15) | reads text, **writes none**: typed questions over a *state* — yes/no, a choice, a score — each answered with a **probability**, in 70–500 ms | a claim's *support* is exactly a yes/no with a probability: *does this quote support this claim?* Its confidence errors are reported far below chat models' — the calibration §7 of the roadmap says a model's self-reported confidence lacks | hosted API only, early access, no weights to pin (ADR 0031 cannot apply); cannot extract spans or write the statement; weak with numbers by its own documentation; English first; not deterministic; prompt injection through the judged document succeeded ~59 % of the time in Check Point's test |
| **F · JEPA** | LeCun's *Joint Embedding Predictive Architecture*: V-JEPA 2 (video, robot planning), VL-JEPA (vision-language, predicts **embeddings** of the answer rather than tokens) | — | not a text extractor: it predicts representations, not a JSON record with a quote; nothing released does this job. A research direction worth an aside, not a candidate |

### Where a decision model fits, and where it does not

Jev cannot be the extractor: extraction needs a **quote** and a **statement**,
and Jev returns neither. But the claim model has a number no generative model
gives honestly — `Evidence.Support`, *how well this quote supports this claim* —
and that is a yes/no with a probability, which is precisely Jev's shape. So the
fit is a **second port**, not a replacement: a generative model (or the rules)
**proposes** claim and quote, code **checks** the quote is verbatim and the value
parses, a decision model **scores** the support, a person **approves**. Each step
does one thing, and the weakest-evidence display rule gets a number that means
something.

Against it, for this project today: a hosted service where every other model
runs beside the app, nothing to pin, early access. The project's habit for tools
with credible alternatives is to put them **behind a port and compare them**,
as the browser telemetry did with OpenTelemetry and Faro — so the question is not
*Jev or a local model* but whether support scoring gets its own port with a
local adapter (the same small model, asked a yes/no and read by its token
probability) and Jev as the second adapter to measure against.

### How the big shops got here, and why their tools are not candidates

Asked by the author on 2026-09-26 — *does anybody really do this? Amazon?* — and
then *would OpenTag or the other one make sense?* They would not, and they are
the lineage of the problem:

| year | what | why it is not a candidate |
|---|---|---|
| 2018 | **OpenTag** (Amazon, KDD): attribute values tagged word by word in product text, a model trained per attribute | a research model, not a release; it needs thousands of hand-labelled examples per attribute, which only a catalogue the size of Amazon's has |
| 2020 | **AutoKnow** (Amazon, KDD): a product knowledge graph built from the catalogue, attributes cleaned and completed from text | a whole internal system over hundreds of millions of products, not something to install |
| 2021 | **MAVE**: a public benchmark of attribute values over Amazon products | usable only to evaluate — in English, in other categories, and [arXiv 2607.22949](https://arxiv.org/abs/2607.22949) found 23.2 % of its ground truth noisy against modern models; our own golden set, with written criteria, is worth more here |
| 2023–24 | Generative models without training: Amazon drafts listings for sellers, **who review and approve them**; Walmart reported improving some 850 million catalogue data points with generative AI | the same pattern as invariant 12 — the AI proposes, a person approves — at a scale and with models Tendero does not have |
| 2026 | A 4B open model on a laptop, its answer held to a JSON Schema: precision 0.92 on this golden set (§6) | this spike |

What only Amazon could afford in 2018 a laboratory shop does today in 5 GB of
memory. What Tendero adds is not the extraction: it is **showing the evidence** —
the quote, the support, who approved it — to the shopper and to the agents, where
the big shops use the same technique inside and show only the result.

### The models, for C and D

All permissively licensed at the family level; each must be **re-checked at
the exact revision** before it is adopted (ADR 0031, rules 1–3).

| family | small sizes | licence (family) | note |
|---|---|---|---|
| Qwen3.5 (Alibaba, Feb 2026) | 0.8B, 2B, 4B, 9B | Apache-2.0 | strong multilingual; newer Qwen releases exist above 27B |
| Gemma 4 (Google, Apr 2026) | E2B, E4B | **Apache-2.0**, the first Gemma under it | earlier Gemma licences were not permissive |
| Phi-4-mini (Microsoft) | 3.8B | MIT | weaker in Spanish, to be measured |
| SmolLM3 (Hugging Face) | 3B | Apache-2.0 | training data and recipe published |

### The finding that changes the plan: nobody official publishes the GGUF

On 2026-09-26 neither the Qwen organisation nor Google publishes a GGUF of these
small models. The GGUFs on Hugging Face are **third-party conversions**
(unsloth, bartowski). ADR 0031 rule 5 — *a copy re-packaged by a third party is
a different artefact from the revision that was checked* — rules them out as
they are.

The way through keeps the rule: **convert it ourselves**. Download the official
`safetensors` at a pinned SHA, run llama.cpp's own `convert_hf_to_gguf.py` and
`llama-quantize` at a pinned llama.cpp release, and record the resulting file's
SHA-256. The artefact is then *derived, reproducibly, from the revision that was
checked* — and a startup check can compare the hash of what is served with the
one configured, as `P8-3` does for TEI's `model_sha`.

## 4. Decisions

Each with its date, who took it, and why — as it was taken.

| date | decision | by | why |
|---|---|---|---|
| 2026-09-26 | Phase 10 is built whole on one branch, `feature/phase-10-knowledge` | the author | — |
| 2026-09-26 | **`Microsoft.Extensions.AI.OpenAI` and `OpenAI` approved** (MIT, to be re-checked at the exact version when added) | the author | one `IChatClient` for any OpenAI-compatible server — llama.cpp now, vLLM or a hosted model later — instead of a client of our own |
| 2026-09-26 | **The rules extractor is built whatever the model** | Claude, from invariant 8 | every AI feature has a non-AI path, tested; and it catches the numeric specs exactly, at no cost |
| 2026-09-26 | **JEPA is not a candidate** | Claude | it predicts embeddings, not records with quotes; no released JEPA model extracts text. (Researched first because *JEV* was read as JEPA; the author meant Jev.) |
| 2026-09-26 | **Jev is not the extractor** | Claude | it returns no quote and no text; extraction needs both |
| 2026-09-26 | **No third-party GGUF.** If a GGUF is used, we convert it from the official revision and pin its hash | Claude, from ADR 0031 rule 5 | a re-packaged file is not the artefact whose licence was checked |
| 2026-09-26 | **The spike measures rules, Qwen3.5 4B and 2B, and Gemma 4 E4B** (E2B too, as the same family's smaller point) through llama.cpp **v0.5.0**, each converted from its official revision | the author | the four newest permissive small models, two families, two sizes each: the spike can say whether size or family matters more |
| 2026-09-26 | **Support scoring gets its own port**; a local adapter (the same small model, a yes/no read by its token probability) and **Jev** measured against it | the author | Jev fits `Evidence.Support` exactly; measuring it behind a port costs one adapter, and deciding without measuring would be the opinion this document exists to avoid. Jev needs early access, which the author requests |
| 2026-09-26 | **GLiNER2 is measured** (`gliner2-multi-v1`), in Python, as evaluation only | the author | its output is a span with a score — evidence by construction; adopting it would need a .NET runtime of our own, which the numbers must justify first |
| 2026-09-26 | **The support port also measures an NLI model** (`MoritzLaurer/mDeBERTa-v3-base-xnli-multilingual-nli-2mil7`, MIT) **and, as a counter-example, the reranker already running** (`bge-reranker-v2-m3`) | the author, after asking what they were for | the code can prove a quote exists, not that it says what the claim says; NLI answers exactly *supports / contradicts / says nothing*, locally and pinned. The reranker answers *how related*, and *"no apta para inducción"* is very related to *"apta para inducción"*: measuring it shows why relevance is not support |
| 2026-09-26 | **Every container the spike runs has a memory and CPU limit, and heavy jobs run one at a time** | the author, after the machine ran out of memory | three heavy jobs in parallel left ~3 GB free of 64 and every background task was stopped; the rule now covers the AppHost's containers too, sized from a measurement |
| 2026-09-26 | **GLiNER2 is dropped from the spike** | the author | *"Python doesn't fit the project"*: a candidate whose only runtime is Python would need a .NET runtime of our own to ship, and the author does not want that path open |
| 2026-09-26 | **NLI and the reranker are measured through TEI, not Python**: the reranker as it already runs, and NLI with `MoritzLaurer/bge-m3-zeroshot-v2.0` (MIT) instead of the mDeBERTa one | Claude, following the decision above | TEI is already the stack's model server; `bge-m3-zeroshot-v2.0` is an NLI fine-tune of the embeddings model the shop already runs, an architecture TEI serves, so the candidate measured is one the product could adopt with no new runtime |
| 2026-09-26 | **No Python in the repository, not even for evaluation.** The spike's harness moves to C#, as `SearchEval`'s `claims` suite (`P10-9` brought forward); the Python script that produced the first numbers is deleted, and those numbers are reproduced by the C# suite before they are recorded here | the author | *"why are you writing Python, if it is not necessary and I do not want that code in the project?"* — it was a shortcut: `SearchEval` already is the project's evaluation tool, in the project's language |
| 2026-09-26 | **Measure the makers' own ready formats before converting anything else**: Qwen3-4B and Qwen3-1.7B (Qwen's GGUF) and Granite 4.0 micro (IBM's GGUF); Phi-4-mini's official ONNX waits for the ONNX Runtime GenAI package to be approved; Gemma 4 and NuExtract later, on the numbers | the author, then Claude on the evidence | *"isn't there something already, so we don't reinvent the wheel?"* — there is, and a maker's file satisfies ADR 0031 with no conversion at all |
| *open* | **How the model is served in the product**, and **which model** | the author, on the spike's numbers | — |

### The wheel already exists: formats the makers publish

Found 2026-09-26, when the author asked, halfway through converting Gemma 4 by
hand, *"somebody must have done this conversion before — isn't there something
already, so we don't reinvent the wheel?"* There is. The conversion was only
needed because the spike had picked the **newest** models, and for those no maker
publishes a ready format:

| model | size | ready format | published by | licence |
|---|---|---|---|---|
| Qwen3-4B | 4B | GGUF, Q4_K_M … Q8_0 | **Qwen** (`Qwen/Qwen3-4B-GGUF`) | Apache-2.0 |
| Qwen3-1.7B | 1.7B | GGUF | **Qwen** | Apache-2.0 |
| Granite 4.0 micro | 3.4B | GGUF, Q2 … Q8 | **IBM** (`ibm-granite/granite-4.0-micro-GGUF`) | Apache-2.0 |
| Phi-4-mini | 3.8B | **ONNX**, CPU int4 | **Microsoft** (`microsoft/Phi-4-mini-instruct-onnx`) | MIT |
| Gemma 4 E4B | 4B effective | GGUF | **ggml-org**, llama.cpp's own organisation, not Google | Apache-2.0 |
| Qwen3.5 4B / 2B | — | none official | third parties only | — |
| NuExtract 1.5-tiny / 2.0-2B | 0.5B / 2B | safetensors only | NuMind — **trained for structured extraction** | MIT |

A maker's own GGUF or ONNX satisfies ADR 0031 as it stands — the revision that
was checked is the file that runs, pinned by SHA, exactly as TEI runs `bge-m3`.
ggml-org's Gemma names Google's repositories as its source but not their
revision, which the ADR requires; adopting it would be an amendment and the
author's call. NuExtract is the interesting outsider — small models trained for
this very job — and needs a conversion again.

Converting Gemma 4 E4B by hand also failed twice on the way, and both are
findings: the llama.cpp conversion image of the day (v0.5.0, and `latest`) ships
`transformers` 4.57.6, which does not know the `gemma4` architecture five months
after its release; with 5.17.0 it ran out of memory at a 12 GB limit, and again
writing through a temporary file. **The newest models run ahead of their tools.**

## 5. The spike: declared before it runs

**What is measured.** A **golden claim set** annotated by hand over 20 of the
100 products — the seed of `P10-9` — chosen to cover every row of the table in
§1: numeric specs with and without an attribute, booleans in words, properties
in words, and descriptions with nothing to claim. Each candidate runs over the
same 20 descriptions, es and en, temperature 0.

| metric | how |
|---|---|
| precision, recall | against the golden set; a typed value must match exactly, a statement is matched by its quote |
| **quote verbatim rate** | the share of claims whose quote occurs in the description as written |
| valid JSON | the share of answers that parse into our record |
| latency per product | CPU, warm, p50 and p95 |
| memory | resident, loaded and answering |

**Hypotheses, written before any number exists:**

1. **Rules alone** reach recall ≥ 0.9 on the numeric specs that have an
   attribute, and **≤ 0.35 on the whole golden set**.
2. **A 4B model with a grammar** gives **100 % valid JSON**, a quote verbatim
   rate **≥ 95 %**, precision **≥ 0.85** and recall **≥ 0.70**.
3. **A 2B model** loses **≥ 10 points of recall** against the 4B of the same
   family.
4. **On this laptop's CPU**, a 4B model at 4 bits answers a product in
   **≤ 10 s** p50 — slow for a request, fine for a worker.
5. **Quantizing to 4 bits** costs **≤ 3 points** of precision against 8 bits.

6. **The author's, declared 2026-09-26 before these runs: small models are
   enough.** A model the maker publishes ready to run, of **4B or fewer**,
   reaches the 4B Qwen3.5's precision (≥ 0.85) and recall (≥ 0.70) — and on the
   support port, the 568M NLI model already matched the 4B's yes/no (§6).

A hypothesis that turns out wrong stays here as it was written, with the number
that refuted it beside it.

## 6. Results

Measured 2026-09-26 on this laptop's CPU (16 threads, 64 GB), every model through
the port the product will call: `dotnet run --project tools/SearchEval -- --suite
claims|support …` (the harness is `SearchEval`'s `claims` and `support` suites;
a first Python script produced the same numbers and was deleted, at the author's
decision). Every container ran with a memory and CPU limit; reports, every
prediction and its verdict, are in `docs/blog/reports/2026-09-26-claims/`.
Golden sets: `tools/SearchEval/knowledge/claims.json` (20 products, 71 claims, 5
traps) and `support.json` (40 pairs).

### Extraction

| candidate | P | R | new info | typing | spurious | quotes verbatim | language | traps | p50 |
|---|---|---|---|---|---|---|---|---|---|
| rules | **1.00** | 0.30 | 0.09 | 0.94 | 0 | 1.000 | — | 0 | 0 s |
| **Qwen3.5-4B, Q8** (converted by us) | **0.92** | **0.80** | **0.76** | 0.88 | 13–14 | 0.984 | **1.00** | 1 | 29–45 s |
| Qwen3.5-4B, Q4 (converted by us) | 0.85 | 0.82 | 0.80 | 0.88 | 10 | 0.985 | 0.84 | 1 | 25 s |
| Qwen3.5-2B, Q8 (converted by us) | 0.86 | 0.70 | 0.70 | 0.65 | 20 | 0.966 | 0.05 | 0 | 14 s |
| Qwen3-4B, Q8 (Qwen's GGUF) | 0.89 | 0.68 | 0.69 | 0.65 | 6 | 0.963 | 0.76 | 1 | 40 s |
| Granite 4.0 micro 3.4B, Q8 (IBM's GGUF) | 0.86 | 0.69 | 0.67 | 0.65 | 25 | 0.965 | 1.00 | 1 | 38 s |
| Qwen3-1.7B, Q8 (Qwen's GGUF) | 0.90 | 0.61 | 0.63 | 0.24 | 13 | 1.000 | 0.31 | 1 | 16 s |
| Phi-4-mini, int4 ONNX (Microsoft's), in process | 0.59 | 0.27 | 0.22 | 0.18 | 12 | 0.625 | 0.00 | 0 | 22 s |
| Gemma 4 E4B | not measured: no official GGUF; converting it needed a newer `transformers` than the day's llama.cpp image and more than 12 GB |

*Spurious* and *language* were added after the runs that exposed them, and say so.
The two Qwen3.5-4B Q8 figures are the same file under two server configurations.

**Phi-4-mini: the extra brace is a bug in the runtime's chat client, found
2026-09-27.** All twenty answers failed to parse: a complete claims document
followed by one extra `}`, and once a list that ran until the 3,072-token
ceiling. A diagnostic outside the repository ran the extractor's exact prompt and
schema through ONNX Runtime GenAI's low-level API, token by token. Without
guidance, Phi-4-mini does not write JSON at all; with it, the document is
correct — and on the step where the grammar completes, `GenerateNextToken()` ends
generation **without appending a token** (sequence length 645, 646, 647, **647**).
A loop that reads "the last token of the sequence" after each step reads the
closing `}` twice. `OnnxRuntimeGenAIChatClient` at `rel-0.17.0` reads exactly
that way (`GetLastToken(generator.GetSequence(0))`), so every constrained answer
it returns ends with its last token duplicated. Not Phi-4-mini, not the
constrained decoder, not Tendero's code; no upstream issue found on 2026-09-27.
Separately, and Phi's own: on one product it kept adding claims until the
ceiling — the grammar allows an array of any length.

**Is it a bug, or our misuse?** Asked by the author before anything was
reported. The library's own example (`examples/csharp/ModelChat`) calls
`GenerateNextToken()`, **asks `IsDone()` first** and stops without reading, and
reads the step's token with `GetNextTokens()`. The chat client reads
`GetSequence(0)[^1]` **before** asking — the one place that contradicts the
library's documented loop, unchanged on `main`. Knowledge now runs ONNX models
through its own `IChatClient` (`OnnxGenerationChatClient`), which decodes a token
only when the sequence grew; with it **no answer carries the extra brace**. Phi-4-
mini's own result, once readable: 12 of 20 answers parse, the other 8 run to the
token ceiling adding claims; precision 0.59, recall 0.27, a third of its quotes
not verbatim, every Spanish statement written in English. Of the models measured,
the weakest at this job. Reported upstream on 2026-09-27, at the author's
decision: [microsoft/onnxruntime-genai#2633](https://github.com/microsoft/onnxruntime-genai/issues/2633). Its first run also took the machine down: the maker's
`genai_config.json` sets `max_length` to the full 131,072-token context, and the KV
cache is reserved up front (~16 GB); the extractor now sets a ceiling.

### Support

| scorer | AUC | vs contradicts | vs not stated | acc@0.5 | ECE | memory | p50 |
|---|---|---|---|---|---|---|---|
| **NLI, `bge-m3-zeroshot-v2.0` (568M), TEI** | **1.00** | 1.00 | 1.00 | **1.00** | **0.03** | 2.8 GB | **0.07 s** |
| yes/no, Qwen3.5-4B Q8 | 1.00 | 1.00 | 1.00 | 1.00 | 0.02 | ~5 GB | 2.4 s |
| yes/no, Qwen3-4B Q8 | 1.00 | 1.00 | 1.00 | 0.97 | 0.06 | ~5.6 GB | 0.76 s |
| yes/no, Qwen3-1.7B Q8 | 0.97 | 1.00 | 0.95 | 0.95 | 0.06 | ~3 GB | 0.31 s |
| yes/no, Granite 4.0 micro Q8 | 1.00 | 1.00 | 1.00 | 0.55 | 0.38 | ~4.2 GB | 0.87 s |
| reranker, `bge-reranker-v2-m3`, TEI | 0.99 | 0.99 | 0.99 | 0.85 | 0.16 | 3.6 GB | 1.2 s |
| Jev | not measured: waits on early access |

### Against the hypotheses

1. **Held.** Rules: 0.30 recall on the whole set; exact on what they see.
2. **Held**, for Qwen3.5-4B at 8 bits: every answer valid JSON, 98.4 % of quotes verbatim, precision 0.92, recall 0.80.
3. **Held, just.** The 2B lost ten points of recall.
4. **Refuted.** 25–45 s per product, not ≤ 10: a worker's job, never a request's.
5. **Refuted.** 4 bits cost seven points of precision and every language slip.
6. **The author's — split.** Extraction: no ready-made model of ≤ 4B reached 0.70 recall (Granite 0.69, Qwen3 0.68), and one model generation was worth twelve points at the same size. Support: held outright — the 568M NLI model matches or beats every generative model, 35× faster than the best.

### What the numbers say, before any decision

- **The extractor**: Qwen3.5-4B at 8 bits, converted by us from its revision
  (ADR 0031's way), is the only candidate that met every declared bar. The makers'
  own files are one or two points of recall short.
- **The support scorer**: the NLI model through TEI — the model server the stack
  already runs, a model of the embeddings' own family, calibrated, local, pinned.
- **Every model forces attributes** onto claims that have none (6 to 25 per run),
  and invents values outside an attribute's options (`MATERIAL = WOOD`). The code
  that checks a claim refuses a value outside its options; what still parses
  (`DROP_MM = 140` from a weight) is exactly the support port's job.
- **What was pinned** has to include the server's configuration: the same file
  gave three or four different claims out of sixty with one slot of 4,096 tokens
  instead of four of 8,192.

### Provenance

| model | revision | licence at that revision | file measured |
|---|---|---|---|
| `Qwen/Qwen3.5-4B` | `851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a` | Apache-2.0 (`LICENSE`); base Apache-2.0 | converted, Q8 `959b3e3b…`, Q4 `7fa158bf…` |
| `Qwen/Qwen3.5-2B` | `15852e8c16360a2fea060d615a32b45270f8a8fc` | Apache-2.0; base Apache-2.0 | converted, Q8 `854091d6…` |
| `Qwen/Qwen3-4B-GGUF` | `bc640142c66e1fdd12af0bd68f40445458f3869b` | Apache-2.0 | `Qwen3-4B-Q8_0.gguf` `8c2f07f2…` |
| `Qwen/Qwen3-1.7B-GGUF` | `90862c4b9d2787eaed51d12237eafdfe7c5f6077` | Apache-2.0 | `Qwen3-1.7B-Q8_0.gguf` `061b54da…` |
| `ibm-granite/granite-4.0-micro-GGUF` | `ec48475f0c811d812fbfb61975717a9c36eeb652` | Apache-2.0 | `granite-4.0-micro-Q8_0.gguf` `a023da9d…` |
| `microsoft/Phi-4-mini-instruct-onnx` | `fc04c8f93df696602fd9f300a30d1bf2e3081347` | MIT | `cpu-int4-rtn-block-32-acc-level-4` |
| `MoritzLaurer/bge-m3-zeroshot-v2.0` | `9abf1c8aaeb82a2447809c20753ed0b106b76652` | MIT; base `BAAI/bge-m3-retromae` | TEI `cpu-1.9.3` |
| `BAAI/bge-reranker-v2-m3` | `953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e` | Apache-2.0 (ADR 0031) | TEI `cpu-1.9.3` |
| `google/gemma-4-E4B-it` | `ee0ef6023621cff504d758262d4e04895a5af4a2` | Apache-2.0 (model card) | not converted |

llama.cpp `server-v0.5.0` (`sha256:a94b642b…`) served, `full-v0.5.0`
(`sha256:386ead2a…`) converted; `-c 4096 -np 1 --load-mode none` and
`--chat-template-kwargs '{"enable_thinking":false}'`, temperature 0, seed 42.

## Sources

The author's earlier work on the same ground, which this document reverses and
extends: [Phi-3 writing product descriptions (2024)](https://elguerre.com/2024/07/06/automate-e-commerce-product-descriptions-with-net-microsoft-semantic-kernel-slm-phi-3/),
[a RAG API with Phi-3 and Qdrant (2025)](https://elguerre.com/2025/04/22/%f0%9f%9a%80-building-a-rag-api-with-net-semantic-kernel-phi-3-and-qdrant-enrich-your-e-commerce-experience/),
[ONNX embeddings in .NET (2025)](https://elguerre.com/2025/05/25/implementing-embeddings-via-onnx-with-semantic-kernel-for-local-rag-solutions-in-net/).

Read on 2026-09-26; every licence is re-read at the exact revision before adoption.

- llama.cpp server, JSON Schema → grammar: [tools/server/README.md](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md)
- ONNX Runtime GenAI, constrained decoding with LLGuidance: [PR #1381](https://github.com/microsoft/onnxruntime-genai/pull/1381), [C# API](https://onnxruntime.ai/docs/genai/api/csharp.html)
- GLiNER2: [repository](https://github.com/fastino-ai/GLiNER2), [paper](https://arxiv.org/abs/2507.18546), [ONNX export](https://huggingface.co/onnx-community/gliner2-multi-v1-agent-ONNX)
- Gemma 4 under Apache-2.0: [VentureBeat](https://venturebeat.com/technology/google-releases-gemma-4-under-apache-2-0-and-that-license-change-may-matter)
- Qwen3.5 sizes and third-party GGUFs: [unsloth collection](https://huggingface.co/collections/unsloth/qwen35), [bartowski 4B](https://huggingface.co/bartowski/Qwen_Qwen3.5-4B-GGUF)
- Jev: [Simon Willison](https://simonwillison.net/2026/Sep/21/jev/), [Wavect review](https://wavect.io/blog/jev-ai-decision-model-review/), [Check Point on prompt injection](https://blog.checkpoint.com/ai-security/jev-is-not-a-language-model-but-it-breaks-like-one-prompt-injection-against-a-typed-decision-model/), [Towards Data Science](https://towardsdatascience.com/jev-vs-llms-when-ai-moves-from-generation-to-decision-making/)
- JEPA: [VL-JEPA](https://arxiv.org/abs/2512.10942), [V-JEPA 2](https://ai.meta.com/research/vjepa/)
- On product attribute extraction, evaluation matters more than the model (variance ~23× larger than model choice): [arXiv 2607.22949](https://arxiv.org/abs/2607.22949) — the reason §5 builds the golden set before choosing
