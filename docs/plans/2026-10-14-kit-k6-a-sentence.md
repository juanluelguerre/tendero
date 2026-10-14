# K-6 · A sentence fills the kit's form

**Phase 13c, after K-5.** Spec: `docs/specs/kit-solver.md`, *Surfaces* (2).
Written on 2026-10-14 while phase 13b's mutation run held the source tree —
nothing under `src/` was touched for this plan, and nothing here is built yet.

## What it is

A box above the kit's form: *a kitchen kit for induction, under 200 euros*.
A model turns the sentence into what the form holds — which kit, a budget,
the requirements — and **the form is filled with what it understood, for the
shopper to see and correct before anything is built**. The model parses; the
solver still chooses (invariant 12). No model, an unreadable answer, or a
sentence that names no kit: the form is there, as it was.

## One decision, the author's — taken: option A (2026-10-14)

**Where the parse lives.** It needs two things no project has together: the
model port (`AI`) and the shop's kit templates (`Search`). `Search` does not
reference `AI`; `Assist` references `AI` and the kernel and may reference no
context (an architecture rule computes it).

| Option | What it costs |
|---|---|
| **A · A slice in `Assist`, reading the templates through an `IReadTool` Search provides** (`list_kits`). Recommended | One read tool in Search (the templates are already a query), one slice in Assist. Nothing new is referenced: this is ADR 0054's own seam, and Tino's parsing already lives beside it |
| B · `Search` references `AI` | One project reference, and the solver's project — whose claim is that it is solved, not generated — gains a model. The rule that keeps `Search/Kits` pure would need a carve-out |
| C · In the API host | The host composes and holds no logic; a prompt and a parser are logic |

The spec left the same question open for the *rewording* (decision 6). Option
A answers both: a reworder is a second slice beside this one, if it is ever
wanted.

## The contract

`POST /api/kits/understand` — body `{ sentence }`, `?culture=`; public and
rate-limited as `build` is (thirty a minute a client), because it costs a
model call.

```json
{ "template": "KITCHEN_STARTER", "budget": { "amount": 20000, "currency": "EUR" }, "requirements": ["INDUCTION"], "understood": true }
```

- `template`: one of the shop's codes, or null. **A closed question in plain
  text** (`ClosedQuestion.AskAsync`), the options being the templates' names
  and *none of these* — measured on this model, a closed choice under a JSON
  schema is right 68.0 % of the time against 80.5 %.
- `budget` and `requirements`: a small schema (composed, not chosen — ADR
  0055). A requirement is kept only if the chosen template offers it; what the
  model invents is dropped by code, not trusted.
- `understood: false` with everything null when no model answers, the answer
  cannot be read, or no kit is named. **Not a refusal**: the page shows the
  form. A sentence over the length limit is a 400 problem, `Invalid`.

## In order

1. **The read tool** — `ListKitsTool` in `Search/Features`, audience shopper:
   the templates with their requirement labels. Its test beside
   `SearchProductsToolTests`.
2. **The slice, tests first** — `Assist/Kits/UnderstandKitSentence`: with a
   scripted model (`ScriptedChatClient`), a sentence that names a kit, a
   budget and a requirement; one that names no kit; a model that answers a
   kit the shop does not have; a requirement the kit does not offer; a model
   that is down. Each red first.
3. **The endpoint**, its rate limit, its refusals declared (`Refuses`), the
   OpenAPI document and the frontend's types.
4. **The fixture** — a step in `tino.script.json`'s neighbour for the browser
   spec: no model fits a runner, so the wiring is tested with a scripted
   answer (as `Tendero__StewardScript` does for Steward).
5. **The page** — the box above the form; the store's `understand(sentence)`
   fills `draft.budget`, `required` and, when the kit named is another one,
   goes to it. `kit_built.source` is `sentence` when the form was filled that
   way and built without being touched.
6. **The degradation, tested**: with the model off the box is not drawn, and
   the form is exactly the form of K-5.
7. **The golden set** — a dozen sentences in es and en with the request each
   should become, run against the local model at the phase's close, as the
   copilot's is: how often the kit, the budget and the requirements are read
   right. The number goes in the spec.

## Not in this step

The rewording of the explanation (decision 6, still open), and `build_kit`
over MCP (K-7).
