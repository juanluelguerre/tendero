# ADR 0063 — A picture is the shopper's question, never a fact

**Status**: accepted (2026-10-20, at the close of phase 14b, the camera search's stage 1)

## Context

Phase 14b let a shopper search the shop with a photograph from the search box
(`docs/specs/camera-search.md`, stage 1): the objects in it found by a detector
(ADR 0062), each one searched by its crop with SigLIP 2 (ADR 0058), the results in a
block per object, and one line naming what the shop does not sell. Stage 2, which
the author has not decided, would let Tino take the same picture.

A picture is the first input to the shop that a model reads and the shopper did not
type. Three things could go wrong with it, and none of them is a bug a test of the
endpoints would see:

- **A read said as a fact.** *This is a navy linen shirt* is a claim about a
  product, and claims carry a source and a confidence and are approved by a person
  (ADR 0018). A detector's box is neither.
- **A picture that gives orders.** A photograph of a page reading *ignore the
  above and…* is text a reading model may obey.
- **A picture kept.** A shopper's photograph of their home is personal data the
  shop has no reason to hold.

The spec ruled the answer as decision 1, *what a picture is*. This ADR records it
as built, because stage 2 and every later reader of pictures (barcodes for Steward,
pictures read into claims) must start from it.

## Decision

**A picture is the shopper's question.** What the shop makes of it is reads done by
code, and each read is labelled as what it is:

- **Where things are and of what kind**: boxes from the detector, over a closed list
  of kinds, the shop's categories and a fixed list of common objects it does not
  sell (`src/Search/Images/object-kinds.json`). The detector answers one of those
  kinds or nothing. It is never asked an open question.
- **What looks like each object**: the nearest products by likeness to its crop.
  The sheet says *similar in the shop*, never *this is*. A product's facts on a card
  are the catalogue's, read as any search reads them.
- **What is not sold**: the names of the kinds not sold, from the same file, in the
  shopper's language. They are a kind's name, never a description of the picture.

**Nothing a model reads in a picture is written anywhere.** No claim, no attribute
and no product field comes from a shopper's picture. The only way a read becomes a
fact is the claims' way (ADR 0018), and a shopper's picture is not its source.

**A model that writes words never receives the picture.** In stage 1 no language
model is involved: a detector returns boxes, an embedding returns a vector, and code
draws the sheet. Should stage 2 be built, Tino's answering model gets the reads as
tool results, and any question about the picture is a closed question
(`ClosedQuestion`, ADR 0055): a picture with words in it reaches nothing that can
obey them.

**The picture is not kept.** It is scaled down to 1,536 pixels in the browser and
sent to find its objects. Each object is then searched by its crop. The server
decodes both in memory and discards them: never the image store, a log, a trace or
telemetry. A span carries its size and type (CS-7, `docs/privacy.md`).

## Consequences

- **The sheet can be wrong without saying anything false.** A detector that misses
  the t-shirt under a backpack's strap (the author's photograph, below 0.4) gives a
  shorter answer, not a wrong one. Marking a part is the shopper's correction, and
  the tracking plan counts how often it happens (`search_by_image_submitted.marked`).
- **The line of what is not sold is the only sentence about the picture itself**,
  and it is built from a fixed list. A kind added to that list is a translation
  added in es and en, held by `ObjectKindsTests`.
- **Stage 2 inherits this ADR, not a free hand.** `read_picture` asks only what a
  category declares a picture can show (ADR 0056), as closed questions, and Tino's
  answer says what was read as read (*En tu foto: …*, decision 2, proposed).
- **A hosted model reading shoppers' pictures is a decision of its own** (decision
  6, proposed): this ADR does not allow it, and `docs/privacy.md` would have to say
  so first.
- **Cost**: a shop that never says what is in a picture cannot answer *what is
  this?* That question is not a search, and the shop does not answer it.
