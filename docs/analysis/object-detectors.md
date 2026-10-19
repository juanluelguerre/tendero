# Object detectors for the camera search — measured before one is chosen

**Date**: 2026-10-19. **Spec**: `docs/specs/camera-search.md`, CS-2 (decision 4: *measured
before it is chosen*). **Suite**: `dotnet run --project tools/SearchEval -- --suite objects`
(`tools/SearchEval/Suites/ObjectsSuite.cs`). **For**: the author, who chooses the detector of
stage 1 (the search box) with this table.

## What was measured

Three candidates behind one port, `IObjectDetector` (`src/Search/Images/`), each given the same
37 kinds (`tools/SearchEval/Objects/kinds.json` then, `src/Search/Images/object-kinds.json` since: 24 of the shop's categories a photograph can
show, 13 common objects it does not sell) and asked about the same 136 photographs:

| Candidate | What it is | Runs on | New dependency |
|---|---|---|---|
| `siglip-grid` | the visual search's own SigLIP 2, asked about 35 cells of the picture (whole, halves, thirds) | the API's CPU, in process | none |
| `owlv2` | OWLv2 base (`google/owlv2-base-patch16-ensemble` at `cfd3195b`, Apache-2.0), an open-vocabulary detector; our ONNX export, held to the original (logits within 0.008, boxes within 0.00016) | the API's CPU, in process | **yes**: 360 MB of weights, tier 1. On trial in `artifacts/owlv2-trial/`, outside `src/` and git (chosen since: `models/owlv2`, ADR 0062) |
| `vision-model` | the local Qwen3.5-4B with its projector, asked for boxes as JSON | the GPU, the llama.cpp container Tino and Steward share (one slot) | none |

The photographs, reported apart because studio pictures flatter a detector:

- **ABO, 130**: single products on white from Amazon Berkeley Objects (CC BY 4.0), 100 of the
  shop's kinds in 20 categories and 30 of kinds it does not sell, labelled from ABO's product
  types (`tools/SearchEval/Objects/abo.json`; chosen by `tools/picture-eval/choose.mjs`, 19 left
  out by eye with a reason each, `abo-excluded.json`).
- **The author's, 6**: real scenes at home, 17 objects in all (`tools/SearchEval/Objects/author.json`,
  the pictures under `design/unpublished/camera-photos/`, no metadata): the author's desk (six kinds
  the shop does not sell), a shelf, figures on a chest of drawers, dumbbells (nothing on either list:
  everything found there is invented), and the author in a t-shirt, with and without a backpack.

Every photograph was redrawn as the shopper's browser will send it: upright, at most 1,536 pixels,
no metadata. Each detector ran alone, on a laptop with an RTX 5070 Laptop GPU and 16 logical
processors; the CPU detectors use half of them, as SigLIP does in the shop.

## The numbers

**The likeliest kind** — what the photograph is of, at any score:

| detector | ABO: right | of the shop's kinds | of kinds not sold | the author's: right | median | p90 |
|---|---:|---:|---:|---:|---:|---:|
| siglip-grid | 120 of 130 | 93 of 100 | 27 of 30 | 4 of 6 | 5.6 s | 7.2 s |
| **owlv2** | 117 of 130 | 87 of 100 | 30 of 30 | 4 of 6 | **2.4 s** | **2.9 s** |
| **vision-model** | **125 of 130** | **97 of 100** | 28 of 30 | 4 of 6 | 2.5 s | 3.2 s (10.1 s on the busy scenes) |

**The objects** — what the search would show, at each floor:

| detector | floor | ABO: found | invented a photo | the author's: found | invented a photo |
|---|---:|---:|---:|---:|---:|
| siglip-grid | 0.3 | 33 of 130 | 0.00 | 1 of 17 | 0.00 |
| owlv2 | 0.3 | 109 of 130 | 0.20 | **13 of 17** | 1.50 |
| owlv2 | 0.5 | 76 of 130 | 0.05 | 7 of 17 | 0.17 |
| vision-model | any | **127 of 130** | 0.08 | 12 of 17 | 1.50 |

The full reports, per kind and per photograph, are written by `--report`; this run's are in
`artifacts/picture-eval/` (outside git).

## What the numbers say

- **SigLIP over a grid is out.** It knows what a photograph is of (120 of 130) but cannot place
  anything: its probabilities sit far under any useful floor (33 of 130 found at 0.3, 1 of 17 in the
  scenes), its boxes are grid cells, and it is the slowest. It was the reference to beat, and both
  others beat it.
- **The Qwen recognises best** — 97 of 100 of the shop's kinds, 127 of 130 found with almost
  nothing invented — **and has no score**: a floor separates nothing, so what it invents in a busy
  scene (1.5 a photograph) cannot be filtered. On the busy scenes it repeats an entry until its
  tokens run out (10 s), which the reader survives but the shopper waits for. It needs the GPU, and
  a shopper's search would queue behind a Steward or Tino turn in the one slot.
- **OWLv2 places best and is the only one with a score that works**: at 0.3 it finds 13 of the 17
  objects in the scenes, at 0.5 it invents almost nothing (0.17 a photograph) and finds 7. It runs on
  the CPU in 2.4 s and queues behind nothing. It is weaker on what a phrase describes badly: *plant
  pot* found none of ABO's empty planters (0 of 5), *towel rail* 2 of 5.
- **The words are half of the measurement.** As one query, *bed or chest of drawers* matched neither
  well; asked as alternatives, OWLv2 went from 110 to 117 of 130 (`ObjectKind.Alternatives`). The
  scenes show the next gaps: the figures are not a *teddy bear or doll* and the framed print is not a
  *vase or mirror*, so every detector missed both — words to add (*figurine*, *picture frame*), not a
  detector's fault.
- **Six photographs are few.** The scenes' figures move by a whole object at a time; they show the
  direction, not a precise rate.

## Found while measuring

- **SigLIP's probability saturates**: chosen by probability, every matching cell tied at 1.0 and the
  whole picture won; the cell is now chosen by the cosine (a test held it).
- **The Qwen answers in its training format whatever it is asked** (`bbox_2d`, `label`), labels a box
  with the question's whole line (`"BACKPACK: backpack"`), and on a busy picture loops until cut off.
  Read naively it scored 43 of 130; read as it answers, 125. Each of those is a test now.
- **ABO's product types are noisy** (ground coffee under COFFEE_MAKER, socks under LUGGAGE); a listing
  is taken only when its English name says what it is, and the 130 were looked at by eye.

## Recommendation

**OWLv2 for the search box (stage 1)**, at a floor between 0.3 and 0.5, chosen with more of the
author's scenes. It is the only candidate that meets stage 1's premise — the CPU, no GPU, no queue
behind the agents — and the only one whose score can trade what is found against what is invented,
which is exactly the line under the results (*no lo vendemos*) depends on. Its cost is a dependency:
360 MB of Apache-2.0 weights, converted by us (ADR 0031's way) and pinned.

**The Qwen stays as the second adapter**, as Parakeet stayed beside Whisper. It is the better reader
of a single product, and stage 2 (Tino) already needs the GPU it runs on; whether Tino's reading of a
picture uses it is stage 2's question.

**Before the floor is fixed**: the kinds' words for the gaps above (*figurine*, *picture frame*,
*planter*), and a few more scenes from the author, then the suite again at 0.3, 0.4 and 0.5.

## After the choice: the words filled out, the floor narrowed

The same day, OWLv2 from `src/` (`Owlv2ObjectDetector`, the shop's export and its own tokenizer)
with three words added to the kinds — *figurine* to the toys, *picture frame* to DECOR,
*planter or houseplant* to PLANTS — and a floor of 0.4 between the two (`report-owlv2-words.md`):

| source | floor | found | invented a photo |
|---|---:|---:|---:|
| ABO | 0.3 | 109 of 130 | 0.20 |
| ABO | 0.4 | 98 of 130 | 0.10 |
| ABO | 0.5 | 76 of 130 | 0.05 |
| the author's | 0.3 | **15 of 17** (was 13) | 1.50 |
| the author's | 0.4 | 13 of 17 | 0.83 |
| the author's | 0.5 | **9 of 17** (was 7) | 0.17 |

- **The words found the figures and the print**: the scenes rose by two objects at every floor.
  ABO's single products did not move (117 of 130): what they miss is not words.
- **An empty planter is a vase** to OWLv2: still 0 of 5, four of them read as DECOR. It is a fair
  confusion — on white, with nothing in it, a planter *is* shaped like one — and SigLIP finds the
  products like the crop whichever of the two kinds it is labelled. Left as it is.
- **The dumbbells' case is a suitcase** at 0.46: the one scene with nothing on either list still
  invents an object at 0.4, and none at 0.5.
- **A seventh scene, the author's selfie with a book** (added the same evening,
  `report-owlv2-me-book.md`): t-shirt and book both found. With it the scenes read 17 of 19 at
  0.3 (1.43 invented), **15 of 19 at 0.4 (0.71)**, 9 of 19 at 0.5 (0.14).
- **Proposed floor: 0.4** — taken by the author, provisional — until more scenes are measured: it keeps three of every
  four objects in a real scene and invents less than one a photograph; 0.5 halves what is found.
  It becomes a setting with the endpoint (`Search:Objects:MinScore`), never a constant.

## Decision for the author

**Taken on 2026-10-19 as proposed** (ADR 0062): OWLv2 for the search box, its weights tier 1; the
trial export under `artifacts/owlv2-trial/` replaced by `src/AppHost/scripts/prepare-owlv2.py`, which
exports the text tower too so the kinds stay configuration.

| Question | Proposed |
|---|---|
| The detector of the search box | **OWLv2**, its weights added as a tier-1 dependency (Apache-2.0, revision `cfd3195b`), converted by a script in `src/AppHost/scripts/` as SigLIP's is |
| The floor | chosen between 0.3 and 0.5 after the words are fixed and a few more scenes are measured |
| The Qwen's adapter | kept, measured, not wired into the search |
| SigLIP's grid | kept as the reference in the suite, not wired anywhere |
