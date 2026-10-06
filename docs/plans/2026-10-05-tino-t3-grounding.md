# Tino T-3 · Grounding — Implementation Plan

**Goal:** a reason is shown only when the fact it cites **says it**. T-2 checks that the citation exists and is the right product's; the first real turns showed what that leaves through — *"Está hecho de acero inoxidable"* cited to *piezas: 3* (`docs/analysis/tino-models.md`). T-3 adds the spec's second and third checks, and confirms a *sin dato* by code.

**Spec:** `docs/specs/tino.md` — *Grounding, before anything is shown*.

## Global Constraints

- "Its figures are the fact's. Every number in the sentence must appear in the cited fact. … a digit is compared, not scored."
- "The fact says it. The support port scores *fact → sentence*; under the threshold the reason is dropped." "The threshold is fixed from a measurement."
- "A dropped reason is counted, never shown, never silently replaced."
- "`noData` is offered by the model and confirmed by code: it stands only if no fact read this turn supports the opposite."
- The support scorer down is a cause of no answer of its own, and the storefront shows the search (invariant 8).
- No new dependency. `dotnet build -warnaserror` clean. No attribution trailers.

## Rulings taken in this plan

1. **The premise is the product's name and the fact**, not the fact alone. Measured first (Task 1): a reason says *"Es un termómetro de cocina de sonda hecho de acero inoxidable"* and the fact is *material: acero inoxidable* — the fact alone does not entail the sentence, the product's name and the fact do. The name is the search's, read this turn, never the model's.
2. **Figures are compared against the same premise**: a number in a reason must be in the fact or in the product's name (*"sartén de 26 cm"*).
3. **The threshold starts at 0.5**, from Task 1's pairs, as `Tino:SupportThreshold`. T-5's suite fixes it.
4. **A *sin dato* carries the claim it denies.** The model writes, beside what was asked about, the statement the details would have to say (*"Puede ir al horno"*). Code scores each fact of that product against it: if one says it, the answer is not *sin dato* — it becomes that product with that claim as its reason, citing that fact. What this cannot catch is a fact that says the **opposite** (*apta para horno: no*): the port answers "does it say this", not "does it deny it". Written down for T-5.
5. **No scorer, no Tino.** With no support scorer registered Tino is not available (`GET /api/assistant/status`) and a turn ends `Unavailable / Scorer` before any model is asked; a scorer that fails mid-turn ends the turn the same way, and the fallback profile is not tried — it would need the same scorer.
6. **Scored one after another**: twelve reasons at 60 ms are under a second, and the scorer's container takes one request at a time.

## Tasks

- [x] **1 · Measure.** The scorer on reasons from the real turns, good and bad, with the fact alone and with the name before it; a first threshold. Into `docs/analysis/tino-models.md`.
- [x] **2 · Figures.** `Figures.AllIn(sentence, premise)`, pure, with its tests.
- [x] **3 · Grounding.** `Grounding.HoldAsync(body, sheet)`: figures, then support; dropped and counted; `noData` confirmed. Tests over a scripted scorer; hand mutations.
- [x] **4 · In the turn.** The scorer registered where both Knowledge and Assist reach it (`AI`), the turn's cause `Scorer`, the status endpoint, the AppHost starting the scorer with Tino.
- [x] **5 · Seen running**, the same six questions, and what changed. Docs, the board.

## As built (2026-10-05)

All five, in one commit. The measurement came first and changed ruling 1 from a
guess to a number: the name and the fact kept 17 good reasons of 18 where the fact
alone kept 11. Two limits found on the running stack, in
`docs/analysis/tino-models.md`: what the product's name says passes under any
fact's chip, and a fact that denies a *sin dato* is not seen.
