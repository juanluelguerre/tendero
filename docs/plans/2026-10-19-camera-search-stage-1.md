# Searching with the camera, stage 1 — the plan of the run

**Spec**: `docs/specs/camera-search.md`, stage 1 accepted on 2026-10-19 (the
flow, the two sources of photographs, the detector measured before it is
chosen). Stage 2, Tino, is not in this run.
**Branch**: `feature/camera-search`, from `develop`; merged into `develop` on 2026-10-20.
**Phase**: 14b, numbered and closed on 2026-10-20 (ADR 0063 at the close).
**Waiting on the author**: eight to ten photographs (scenes, an accessory to
mark) in `artifacts/camera-photos/`. Everything that does not need them goes
first; the detectors' comparison waits for them, because scenes are what
separates the candidates.

## Where things live — decided here

| Piece | Where | Why |
|---|---|---|
| `IObjectDetector`, the kinds, the fake | `src/Search/Images/` | Beside SigLIP and `UploadedPicture`: detection is a stage of visual search, and `Search` does not reference `AI`. The spec placed it in `AI`; amended |
| The SigLIP grid adapter | `src/Search/Images/` | It uses `IImageEmbeddingGenerator`, already there |
| The Qwen adapter | `src/Search/Images/` | Over `IChatClient` (Microsoft.Extensions.AI, already referenced by Search), the local profile with its projector |
| The OWLv2 adapter | a trial first, outside `src/`: the export script and the weights under `artifacts/` | A dependency the author approves only if it wins; nothing of it enters `src/` before |
| The common objects not sold | configuration beside the detector, es and en names | Reviewed when a category is added |
| The ABO cases | `tools/SearchEval/Objects/abo.json`: image ids and the expected kind, no image | ABO stays where Amazon hosts it; `tools/picture-eval/prepare.mjs` fetches and redraws them into `artifacts/picture-eval/` |
| The author's photographs | originals in `artifacts/camera-photos/` (ignored), redrawn copies in `design/unpublished/camera-photos/` | Their EXIF holds a location; the copies hold none |

## Steps

1. **ABO's cases.** Listings and image metadata into `artifacts/`; a table from
   ABO's product types to the shop's categories; per category up to eight
   listings with a main image of at least 1,000 pixels, chosen by a seeded
   shuffle so the set is reproducible; plus kinds the shop does not sell. The
   manifest committed, the images not.
2. **The prepare script.** `tools/picture-eval/prepare.mjs`: ABO's images fetched
   by id, and the author's originals, redrawn in Chromium to 1,536 pixels with
   their orientation and no metadata — what the shopper's browser will send.
3. **The port, test first.** `IObjectDetector` (a picture and kinds in, boxes
   with a kind and a score out), `DetectedObject`, the kinds (sold and not
   sold), a deterministic fake, an abstract contract suite, and the rules the
   spec fixes over any detector: one box a kind, eight in all, overlapping boxes
   of a kind merged, the score floor.
4. **The SigLIP grid adapter**, held to the contract suite.
5. **The Qwen adapter**, held to it where the local model runs (skipped with a
   reason elsewhere).
6. **The OWLv2 trial**, outside `src/`.
7. **The measurement**: `SearchEval --suite objects` over every adapter and both
   sources, reported apart, into `docs/analysis/object-detectors.md`. ABO's half
   can run before the author's photographs arrive; the comparison is written
   when both are in.
8. **The author chooses.** Then the endpoint, `q` and `category`, the mock-ups,
   the sheet, the camera — the rest of stage 1, planned again with the choice
   made.

## After the choice (2026-10-19)

The author chose OWLv2 and a floor of 0.4, provisional. Done the same day:
OWLv2 in `src/Search/Images` with its export, tokenizer and tests (ADR 0062); the
kinds in `src/Search/Images/object-kinds.json`, read by the shop and the suite;
`POST /api/search/by-image/objects`; `q` and `category` on `POST /api/search/by-image`,
narrowed through the picture store's `products` field; the status's `objects`.

Next, in order:

9. **The English mock-ups of the sheet** (CS-8), for the author to look at before
   any of it is built: the picture with its numbered marks, marking a part, the
   blocks, the line of what is not sold, the field to narrow; phone and desktop.
   **Drawn 2026-10-19**, screens 8 to 13 of `design/unpublished/mockups/storefront-visual-search.pen`
   (PNGs beside it), on the author's photograph with a backpack: marking a part, one
   block an object, narrowed with words, the phone, the degraded paths, the
   viewfinder. **Accepted by the author the same evening, as drawn.**
10. **The sheet and the camera** in the storefront (CS-1, CS-3, CS-6, CS-8), its
    degraded paths as browser specs (CS-9), the tracking event (`picture_search_used`)
    and `docs/privacy.md`.
    **How it is built** (decided while planning it, 2026-10-19):
    - **Two root stores, each one job.** `VisualSearchStore` keeps what it had — whether
      the shop answers pictures, the panel under the box, the page of one list — and
      its `give` now hands the picture to `PictureSheetStore`
      (`visual-search/picture-sheet/`), which holds the sheet: the picture, the marked
      part, the objects, a block's state each, the words. *See all* puts that block's
      list on the existing page (`/search/picture`), so the page stays the one place a
      full list is drawn.
    - **One flow whatever the server has.** With `objects: false`, or the objects
      endpoint refusing, what was sent is one object of no kind searched by likeness —
      screen 12's first case — so the degraded path is the same code with one block.
    - **Pure geometry apart** (`picture-box.ts`): scaling the server's boxes to the
      picture, clamping, moving and resizing with the keyboard, the crop's rectangle —
      tested without a DOM. Cutting the crop is a canvas, as `shrinkPicture` is.
    - **The sheet is a native `<dialog>`** opened modal from the component (a DOM
      effect): focus held inside, Escape closes, the page behind inert; full screen
      under `md`.
    - **The viewfinder** (`visual-search/viewfinder/`) is `getUserMedia` behind the
      panel's *Take a photo* on a phone; refused or absent, the same button clicks
      today's `capture` input.
    - **Events: the existing ones grow rather than a second one** (my call): the spec
      named `picture_search_used`, but `search_by_image_submitted` already answers
      *does anyone search with a picture* with the same `how` and `outcome`, so it gains
      `marked`, `objects`, `not_sold` and `words`, and `…_result_followed` gains `block`.
      Two events for one submission would count it twice.
    - **Correcting a detected box is not in this run**: the approved mock-ups draw
      marking a part before sending, not dragging a box the detector drew. CS-3's
      correcting line waits for the evidence (`marked` counts how often a part is marked).

    **Built the same evening.** Storefront unit tests green (the sheet's store 21, the
    geometry 5, the panel and the root store rewritten for the hand-off); the browser
    suite 51 passed, 14 skipped as before, with `visual-search.spec.ts` (eight) and
    `camera.spec.ts` (two, Chromium's fake camera). Then the stack with
    `Tendero__Vectors=true` and the real detector: the author's book selfie answered
    with t-shirts and *no están en la tienda: libro*; the backpack photograph with
    backpacks and *reloj*, its t-shirt — crossed by the strap — under 0.4 (captures in
    `design/unpublished/camera-search/`). Found by those captures: a token named
    `sheet` drew the dialog 920 px wide, because `max-w-sheet` resolves to
    `--spacing-sheet` first; renamed `--container-picture-sheet`.
    `detect_objects` and `find_by_picture` as `IReadTool`s are left for stage 2, their
    only reader (the spec said now; moved, and said so in it).

11. **`SearchByImage`'s refusals to ADR 0059** (found by the conventions review): it
    still answers `ValidationProblem` and an uncoded problem in English, while its
    sibling for the objects answers coded problems in es and en. `UploadedPicture.Check`
    already says both; the change moves the storefront's handling of those errors with it.
    **Done 2026-10-19.** `PictureRefusals` (`Search/Images`, down from the slices, as a
    port is): `PictureNotTaken` shared by by-image and its objects sibling, so one picture is
    refused with one code; `VisualSearchNotAvailable` and `VisualSearchNotAnswering` for
    by-image and words against the pictures, both declared with `Refuses`. The picture is
    checked at the endpoint, before dispatch, and a body that does not decode is
    `UnreadablePictureException`, refused as the picture. The validator keeps the question's
    other fields. The storefront: with no detector, a picture by-image refuses is the sheet's
    *refused*, not a block that failed. Tests first, red, then green.
12. Stryker scoped over `Search`, and the notebook.
    **Done 2026-10-19.** The 20 files the branch changed, 79.75 % of covered; 28 survivors were
    real and closed by tests red by hand first (about 84.8 % by count), the rest classified in
    `docs/testing.md`, *The camera search's stage 1, scoped*.
