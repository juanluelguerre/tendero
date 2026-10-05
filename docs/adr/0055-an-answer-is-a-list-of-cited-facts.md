# 0055 — An answer to a shopper is a list of cited facts, never prose of a model's own

**Status**: accepted (phase 12b's close, 2026-10-05; the spec's rule, decided with the author before T-1 and measured since)

## Context

Tino (phase 12b, `docs/specs/tino.md`) answers a shopper in the storefront: which
pan works on induction, whether this one can go in the oven, how long a return is
open, where an order is. Steward (phase 12) answers the shopkeeper, and its answer
is a model's words beside rows the shop's code draws — a person who knows the shop
reads them, and a wrong sentence costs a second look.

A shopper is the other case. They do not know the catalogue, they act on what they
read, and what they read is the shop speaking: a model that says a pan is
oven-safe has made the shop say it. Invariant 12 already says a model never writes
to an aggregate; nothing yet said what a model may *say* to somebody who cannot
check it.

Two things were measured before deciding (`docs/analysis/tino-models.md`):

- The local 4B holds a JSON schema — 10 of 10 in the spike — and llama.cpp refuses
  tools and a schema in one call.
- Asked for free prose over the same reads, the same model said things its reads
  did not: a material cited to a piece count, a litre nobody had recorded.

## Decision

1. **An answer is typed, and every sentence in it cites a fact.** The model does
   not write a reply; it fills one of a closed set of kinds — products with
   reasons, the shop, an order of the shopper's, *the details do not say*, a
   question back — and each sentence names the id of a fact a read returned. A
   fact is the shop's: an approved claim, a data-sheet value, a sentence of the
   description, a policy, a line of the shopper's order, a notice.
2. **The schema is built for the turn, from what was read.** Product codes, order
   keys and fact ids are enums of that turn's fact sheet, and a kind with nothing
   to cite is not offered at all. A product the model did not read cannot be
   named; a fact that does not exist cannot be cited. This is grammar, not a
   check afterwards.
3. **A turn is two calls on one profile**: gather, with the shopper's read tools
   and no schema; compose, with the schema and no tools. The second sees the fact
   sheet, not the raw tool results.
4. **Code decides what is shown.** Before anything leaves the server
   (`Grounding`): a figure in a sentence must be in its fact or the product's
   name; the support scorer (the claims pipeline's, ADR 0018) must say the
   product's name and the fact entail the sentence, at `Tino:SupportThreshold`; a
   sentence that fails either is dropped and counted, never rewritten. A *the
   details do not say* carries the claim it denies, and becomes a product answer
   if a fact says it. An answer left with nothing is not an answer.
5. **What a model wrote is never the card.** A product in an answer is the shop's
   own card, read by its code; a price, a name, a picture and stock come from the
   shop. The sentence sits under it with a chip naming its source, which opens the
   fact itself.
6. **No scorer, no Tino; no model, the search.** Without the support scorer Tino
   is not available and says so. When a turn does not answer — no profile, the
   model, the time, a budget, the scorer — the storefront shows the search's
   results for the same words (invariant 8), and that path has a browser test.
7. **What code can say, the model is not asked for**: the guest's notice about
   orders, *the shop has no such information*, the sentence of a *the details do
   not say*. The model supplies what was asked about; the wording is the shop's,
   in es and en.

## Consequences

- Tino cannot be fluent. It cannot compare two products in a paragraph, soften a
  *no*, or answer part of a question and say which part it left: there is no kind
  for it. That is the price, and it is written in the spec's non-goals.
- A sentence can still be wrong in what it means while right in what it cites.
  The measured limits stand as written (`docs/analysis/tino-models.md`): what the
  product's **name** says passes under any fact's chip; a fact that **denies** a
  *the details do not say* is not seen; a card may be shown with nothing said. The
  `assistant` suite holds the first two as cases.
- Every turn costs a second model call and one scorer call per sentence. On the
  local model that is 4 s at the median and 9 to 11 s at p95, of a turn's 20; the
  caps are three products and two reasons, from the measurement, not the four and
  three first written.
- The scorer is now in the path of a shopper's answer, not only of the claims
  pipeline. Its threshold has one set of labels behind it (32 pairs); it is
  configuration, and re-tuning it needs labelled sentences first.
- A new source of facts is a `FactSource` and a read that returns it. A new kind
  of answer is a change to the schema, the grounding, the wire and the panel
  together, and an architecture rule keeps Tino's agent with no proposable
  command: it reads, and it never proposes.
- The rule is Tino's. Steward keeps prose, because its reader can check it and
  its words change nothing until a person approves a proposal (ADR 0019).

## Alternatives considered

- **Prose with citations appended** — what most shopping assistants show. The
  citation is then decoration: nothing ties a sentence to it, and the first real
  turns had sentences citing a real fact that did not say them.
- **Prose, then a judge model** to strike what is unsupported. A second generative
  call to check the first, with its own errors, and on one local slot it doubles
  the wait. The scorer is a 60 ms classification with a number that can be
  measured against labels.
- **No model's words at all** — the model only picks products and facts, and the
  shop prints the facts. Tried in thought, not built: a data-sheet line (*piezas:
  3*) is not an answer to *is it a set?*, and the sentence that turns one into the
  other is the one thing a model is for here.
