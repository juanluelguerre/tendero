# ADR 0062 — The objects in a photograph are found by a detector, and the detector is OWLv2

**Status**: accepted (2026-10-19, the camera search, stage 1)

## Context

The search box takes a photograph (`docs/specs/camera-search.md`): sent as it is,
every object in it is searched, and the results come in blocks, one per object,
with a line for what the shop does not sell. Visual search already exists (ADR
0058): SigLIP 2 answers "which products look like this picture". It does not
answer "what is in this picture, and where", and a desk photographed whole is a
blur of likeness to everything on it.

So a detector was needed, and the spec ruled that it is **measured before it is
chosen** (decision 4). Three candidates went behind one port and through one suite
(`dotnet run --project tools/SearchEval -- --suite objects`), on 130 single
products from Amazon Berkeley Objects and six scenes the author photographed at
home (`docs/analysis/object-detectors.md`):

- **SigLIP 2 over a grid of 35 cells**: no new dependency. It knows what a
  photograph is of (120 of 130) and places nothing (1 of 17 objects in the scenes).
- **OWLv2**, an open-vocabulary detector, on the CPU: 117 of 130, 13 of 17 in the
  scenes at a floor of 0.3, 0.17 invented a photograph at 0.5, 2.4 s.
- **The local Qwen3.5-4B with its projector**, on the GPU: the best reader of a
  single product (125 of 130) with no score to put a floor under, and in the slot
  Tino and Steward share.

## Decision

- **`IObjectDetector` lives in `Search/Images`**, beside SigLIP and the uploaded
  picture: detection is a stage of visual search, and `Search` does not reference
  `AI`. It takes a picture and the kinds to look for — the shop's categories a
  photograph can show and a list of common objects it does not sell — and answers
  boxes with a kind and a score. The rules over any detector (one box a kind, eight
  in all, the floor) are `ObjectsInPicture`'s, not an adapter's.
- **The detector is OWLv2** (`google/owlv2-base-patch16-ensemble` at `cfd3195b`,
  Apache-2.0), chosen by the author on the numbers. It is the only candidate whose
  score trades what is found against what is invented, which the line under the
  results depends on, and it runs in the API's process on the CPU, queued behind
  no agent.
- **Its weights are tier 1** (the licensing policy): converted by us from the
  maker's pinned revision, as SigLIP's (ADR 0031), by
  `src/AppHost/scripts/prepare-owlv2.py`, into `models/owlv2` outside git, and held
  to the original before they are kept. Recorded in `THIRD-PARTY-NOTICES.md`.
- **The kinds are configuration, so the text tower is exported too**, and CLIP's
  tokenizer is ours (`Owlv2TextTokenizer`, over Microsoft.ML.Tokenizers, already a
  dependency), held id for id to the maker's. No kind's embedding is frozen into
  the export; a category added is a word added and the suite run again.
- **A kind is asked as alternatives** (`ObjectKind.Alternatives`): *bed or chest of
  drawers* is two queries and the kind is as likely as the likelier. As one query
  it matched neither well.
- **A keyed adapter, chosen by configuration**, as speech's (ADR 0061):
  `Search:Objects:Detector` names it and `Search:Objects:Owlv2:Directory` is the
  export. The AppHost hands both over only where the export is whole and the
  pictures' model is there, since a detected object is searched with SigLIP.
- **SigLIP's grid and the Qwen's boxes stay as adapters in the suite**, measured
  and not registered. The Qwen is the better reader of one product, and stage 2
  (Tino), which already needs the GPU, decides whether it reads Tino's pictures.

## Consequences

- **Without the export, a photograph is one object**: searched whole, as visual
  search already does (CS-9), and said so. Typing never passes through here
  (invariant 8).
- **The floor is not fixed by this ADR.** It is between 0.3 and 0.5, chosen with
  the suite once the kinds' words have been filled out and more of the author's
  scenes measured; until then it is the spec's to record.
- **615 MB more under `models/`**, and one more conversion on a fresh clone,
  which the AppHost does not run by itself: the folder is the opt-in.
- **OWLv2 does not replace SigLIP or the Qwen** for anything else (the author,
  2026-10-19). Its text side is CLIP's, English only and sixteen tokens, so it
  cannot take a shopper's Spanish; its picture embedding was retrained to place
  patches, not to find similar products; and it answers where, never a question
  about colour or brand.
