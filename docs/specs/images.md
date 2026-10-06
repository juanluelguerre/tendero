# Spec: Images — pictures the shop can read, check and search by

**Status**: accepted (2026-10-06), building — I-1 under way
**Phase**: 13a · **Branch**: `feature/phase-13a-images`
**Slice(s)**: `tools/SeedImages` · `AI` (a model that reads a picture) · `Catalog` (alt text) · `Knowledge` (claims from a picture) · `Search` (by image)
**Article**: *"The catalogue had no eyes"* (working title)

## Problem

The shop sells by pictures and cannot see one. Eighteen of the hundred seed
products have a picture; none has alt text in either language; nothing checks
that a picture shows what its product's data says; and a shopper holding a
photograph of what they want has only words to search with. The evidence that
all of this is feasible on the stack already running is a spike
(`docs/analysis/image-models.md`); this spec is what is built from it.

## Decided by the author (2026-10-06)

| Question | Ruling |
|---|---|
| Which image work | **Both**: a model that reads pictures (alt text, picture against data) and visual search (SigLIP 2) |
| Weights no maker publishes in the format we run | **Our own conversion** from the pinned revision (ADR 0031), for the vision projector and for SigLIP 2's ONNX. Not a third party's copy |
| What a claim read from a picture rests on | **Option A**: the document is the model's description of the picture, the quote is taken from it, and the claim is marked as read from a picture |
| A picture a shopper uploads | Not kept, metadata stripped, written in `docs/privacy.md` |
| Voice | Not here: postponed to a phase of its own (14) |
| What the pictures may cost | **2 € in all**, and drawn **five at a time**, not the 82 at once |
| What a batch is | The five drawn together: the author looks at their contact sheet and accepts or sends back, batch by batch |
| Alt text | In the same batches of five, so a mistake costs five and not a hundred |
| New packages in `src/` | Yes to `Microsoft.ML.OnnxRuntime`, `Microsoft.ML.Tokenizers` and SkiaSharp, each checked at its exact version when added |
| The kit solver | Its own phase, 13c, after this one |

## Behavior

Seven steps, each closed before the next. The order is forced: nothing can be
measured on pictures that do not exist, and the model that reads a picture is the
check the generated ones need.

### I-1 · The 82 missing pictures

`tools/SeedImages generate --take 5` with the `gemini` provider draws what
`seed/images/` lacks, **five at a time**. It is a paid call (about $0.03 a
picture) on the author's renewed key, **under a ceiling of 2 € for all of it**:
at that price the ceiling does not reach 82 pictures and their redraws, so the
spend is counted batch by batch in the table below and the run stops and asks
before the batch that would cross it.

| Batch | Date | Pictures | Calls | Spent (about) | Running total | Outcome |
|---|---|---|---|---|---|---|
| 1 | 2026-10-06 | `B10SHO0102` to `B10SHO0106` | 5 | $0.15 | $0.15 | **accepted by the author**, with `B10SHO0106` to be redrawn: an emblem on its side |
| 2 | 2026-10-06 | `B10SHO0107`, `B10SHO0108`, `B10SHI0201` to `B10SHI0203` | 5 or 6 (one picture took the time of two) | $0.15–0.18 | $0.39 at most | **the first batch through the shop model's five checks: 5 of 5 pass**; shown to the author, waiting |
| 1, redraws | 2026-10-06 | `B10SHO0106`, twice | 2 | $0.06 | $0.21 | the first redraw carried the emblem again — the hand-wash symbol, drawn from *"Hand washable."* in the description; with that sentence kept out of the prompt (`WithoutCareInstructions`), the second is clean |

A picture is accepted only when it is **right for its product's description** and
**in the register of the first eight**. `verify` today checks text, colour, scene
and placement; two checks are added:

- **Picture against description** — a vision model is shown the picture with the
  product's name and description and says what it shows, whether it matches, and
  what differs (count, shape, material, a part the description names). Measured
  first on a set of known-wrong pairs (pictures deliberately swapped between
  products), so how often it misses is a number and not a hope.
- **Same register** — the candidate beside a reference picture: line illustration,
  the tokens' flat background, the same framing.

**Each check proposes; a person accepts the batch.** A flagged picture is redrawn
or the catalogue entry is corrected, whichever is wrong, and each case is written
down with its reason. The whole run is recorded as it happens — prompt, spec,
batches with time and cost, the vision pass, every redraw and why — in the
notebook, because the article and the book have to tell it.

### I-2 · A model that reads a picture

The model the shop already serves (`Qwen3.5-4B`) has a vision tower; llama.cpp
needs it as a second file. That file is **produced by us** from the pinned
revision, as `prepare-extractor.sh` produces the text half, and recorded under
ADR 0031 with its hash.

**If the converter of the pinned llama.cpp does not support this architecture**,
the step stops there and the author is told what was found: a third party's file
is not a fallback this spec may take on its own.

*As built (2026-10-06), the first half*: it does. `src/AppHost/scripts/prepare-vision.sh`
converts the projector from the pinned revision with the pinned converter — 672 MB,
SHA-256 `1a039fd0…b7f5`, recorded in ADR 0031 — and the server loads it beside the
Q8 text half in 5.7 GB of the card. **The image tool asks it first**: `verify` and a
new `pairs` command reach a vision model through a port, `IVisionModel`, with two
adapters (Ollama, and llama.cpp's server — the shop's model), and carry I-1's two
checks. Measured on the 23 pictures there are: all 23 pass the five questions; the
description check refuses no right picture and **misses 3 to 4 wrong ones in 23**,
always a right kind of object with something wrong about it (one pan for a set of
three, a backpack for a duffel) — so a person's look at each batch stays
(`tools/SeedImages/README.md`).

*The second half, the shop's side (2026-10-06)*:

- **`Tendero__Vision=true`** starts the local model with its projector, after a
  `vision-projector` job has converted it (limited to 4 GB: the conversion's own
  memory peaked at 2.6). Only on a GPU, as Tino: it is where it was measured. The
  API is then told the `local` profile reads pictures.
- **`ModelProfile.ReadsPictures`**: declared, because nothing in a profile's
  address says whether its server has eyes. What needs a picture read asks first,
  and takes its path without a model when the answer is no.
- **`Pictures.ForModel`** (`src/AI/Chat`): the one place that turns a stored
  picture into what a model is sent — decoded and written again as PNG, in memory.
- **The scripted client** answers a step marked `"pictures": n` only when the
  message carries that many, so a pipeline that forgot the picture fails in CI.
  **The in-process ONNX client refuses** a conversation with a picture in it; it
  used to drop it and answer anyway.
- **Proved against the real model**: a contract test sends a WebP through the
  shop's own factory to the server started as the stack starts it; the model names
  its colour and the picture's tokens are in `Usage`. It skips where no such
  server runs. The whole chat contract passes on the server with the projector too.
- **Not needed after all**: `Catalog:Images:RootPath` in the AppHost. The spike
  wanted a container to read the store; the API reads it and sends the bytes.

One model serves text and pictures (5.5 GB of 8 with the projector; two do not
fit). What changes in code:

- The store's pictures are WebP and the server does not decode it: a reader
  converts in memory before sending.
- `InProcessOnnxChat` and `ScriptedChatClient` read only a message's text and
  would drop a picture. The scripted one learns to match on a picture's presence,
  so the pipeline runs in CI with no model.
- `Catalog:Images:RootPath` is set in the AppHost so a container can read the store.
- Every call is metered as today; the picture's tokens are already in `Usage`.

**Degraded path**: with no vision-capable profile, everything in I-3 and I-4
says so and proposes nothing; the catalogue and the shop are unchanged.

### I-3 · Alt text, proposed and approved

An image has no alt text in either language. A job reads each picture that lacks
one and **proposes** `SetImageAltText` — a registered command (ADR 0052) with the
text in es and en — which lands in the Proposals queue with the picture beside
it. The shopkeeper approves, edits or rejects; only an approval writes to
`Catalog`, as the person, with an audit row. Steward gains the same command, so
*"write the alt text of this product's pictures"* ends in the same queue (it says
today that it cannot).

The storefront renders the approved text as the picture's `alt`. **The non-AI
path** is the shopkeeper typing it on the product's screen, which must exist and
work with every model stopped.

### I-4 · What a picture says, as claims — and against the data

- **Claims.** `SourceKind.ProductImage` joins `ProductDescription`. Its document
  is the model's description of the picture, stored with the image's id, the
  model and the prompt version; a claim's quote is taken from that description
  and held to it by the support scorer, as a claim from text is. In the review
  queue such a claim is **marked as read from a picture**, with the picture
  shown, because the scorer here checks a model against a model's own words and
  the person is the only independent check. Nothing is approved without one
  (invariant 12).
- **Picture against data.** Where the reading contradicts a catalogue attribute
  (the picture is black, the colour says navy), the review queue gets a flag that
  names both. It never corrects either: which one is wrong is a person's call,
  as it was for `B13BAG0710`.

### I-5 · Image embeddings, behind a gate

**First the tokenizer fixture** (`P13-1`): the text tower's SentencePiece
tokenization must reproduce the reference outputs exactly, or text-to-image
degrades without a sign. Until that test is green, nothing else in this step is
written.

Then `IImageEmbeddingGenerator`, a port with an in-process adapter: SigLIP 2
(`google/siglip2-base-patch16-224`, Apache-2.0, pinned revision) on ONNX Runtime,
**exported by us** from the maker's weights. No Python sidecar. A collection
keyed on `ImageId` — a content hash, so identical pictures are embedded once —
and named for the model, so SigLIP and SigLIP 2 vectors can never be mixed. Fed
by the outbox when a product is published or its picture changes; never inline in
a request.

### I-6 · Searching by a picture

`POST /api/search/by-image`, `AllowAnonymous` by explicit decision, rate-limited
as Tino's turns are: a picture in, the products whose pictures are nearest out,
as the shop's own cards. Then text against pictures, as a separate measured
endpoint — **not** a leg of the hybrid search until its effect on NDCG@10 is known.

In the storefront, the search box gains a way to give it a picture (a mock-up
first, in English). **Degraded path, tested**: with the embedding model or the
vector store down the control says it is not available and the lexical search is
untouched (invariant 8).

**The uploaded picture**: decoded and re-encoded in memory, which drops its EXIF;
bounded in size and type; embedded; discarded. It is never written to the image
store, a log or a trace — a span carries its dimensions and nothing else. `docs/privacy.md` says so.

### I-7 · Close

The phase's ADRs, Stryker over every project it touched with its survivors
classified, the suite's numbers, before and after captures, the notebook and the
book's map.

## Non-goals

- **Voice** — phase 14.
- **The kit solver** — phase 13c.
- A picture in a return request, or any picture of a customer's that is kept.
- Generating pictures as a feature of the shop: `SeedImages` stays an authoring tool.
- Image derivatives, background removal, a CDN: scale work, deferred with its number.
- A second model on the GPU. One serves text and pictures.

## Measures

- **The two new checks** on known-wrong pairs: misses and false alarms, before any
  generated picture is judged by them.
- **Alt text**: every approved, edited and rejected proposal is the measure —
  the share approved unedited, per language.
- **Visual search**: an `images` suite in `tools/SearchEval`. Queries are pictures
  that are not in the index but have a known answer (the same product drawn by the
  other generator, a crop, a rotation); recall@1 and recall@5. Text against
  pictures is scored with the existing golden set and reported beside it, not
  gated.
- **The search gate does not move**: NDCG@10 stays at or above its threshold,
  because nothing here changes the hybrid search.
- Contract tests for the new ports; the degradations as tests, not comments.

## Evidence of use

| Event | The question it answers | The decision its value would change |
|---|---|---|
| `image_alt_proposal_decided` (approved, edited, rejected) | Is the proposed alt text good enough to approve as written? | Below a share approved unedited, the job stops proposing in bulk and proposes on request |
| `search_by_image_submitted` | Does anyone search with a picture? | If almost nobody does, the control leaves the search box for the search page |
| `search_by_image_result_followed` | Did the picture find what they wanted? | Whether text against pictures is worth making a leg of the main search |

## Licensing and dependencies (each needs the author's yes before it is added)

- **Weights, tier 1**: SigLIP 2 base (Apache-2.0) and Qwen3.5's vision projector,
  both at pinned revisions, both converted by us (ADR 0031).
- **Packages, new to `src/`**: `Microsoft.ML.OnnxRuntime` (MIT; `AI` references
  only `OnnxRuntimeGenAI` today), `Microsoft.ML.Tokenizers` (MIT), and SkiaSharp
  (MIT) to decode a picture — `tools/SeedImages` already uses it; not ImageSharp,
  whose licence is not permissive. Each checked at its exact version when added.
- **Paid**: Gemini for I-1 only.

## Open questions

1. **Where the picture control sits** in the search box — a mock-up settles it.
2. **The ceiling against the count.** 2 € is about 65 pictures at $0.03 with no
   redraw, and there are 82 to draw. The author's to say when the total nears it:
   raise it, or leave some products without a picture.
3. **`verify` has no model to ask on this machine.** It wants Ollama with
   `qwen3.5:9b`, and the only model installed there is a 27B that does not fit the
   card. Until I-2 gives the shop's own model eyes, a batch is checked by `probe`
   (pixels) and by a person's look at the contact sheet.

Settled on 2026-10-06, in the table at the top: the ceiling, what a batch is,
alt text by batches, and the packages.
