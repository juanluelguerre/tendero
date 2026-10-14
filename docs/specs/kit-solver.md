# Spec: the kit solver — a recommendation that shows its arithmetic

**Status**: written 2026-10-09 and **accepted the same day**: the author read
it and took its eight decisions as proposed (*Decisions taken with the
author*). Building. Phase 13c of `docs/delivery-plan.md` (`P13-5` to `P13-8`).
**Slice(s)**: `Search` (a pure solver under `Kits/`, the slice `Features/BuildKit`
and its read for agents); `Pricing` through the adapter Search already has;
`Mcp` (one tool); the storefront (a page); `AI` only for the two optional
ends — parsing a sentence and phrasing an explanation.
**Article**: *A recommendation that shows its arithmetic* — the model parses
and phrases, the solver decides.

## Problem

A shopper who wants *a kitchen kit for under 120 €* has to do the shop's work:
open five categories, keep a running total in their head, go back when the
knife they like leaves no room for a pan. A model asked the same thing answers
at once and cannot be trusted with it — it picks products by how they read,
adds prices approximately, and explains its choice with reasons it made up
afterwards. Nothing about that answer can be tested.

The shop has everything a right answer needs — what each product is, what it
costs, whether it is in stock, what works on an induction hob — and no feature
that puts them together. This is differentiator 5: **a recommendation
constrained by a budget, chosen by code, that says why each thing is in it and
what was left out**.

## The rule

**The model never selects a product, never adds a price and never gives a
reason.** Four stages, and a model is allowed at the two ends only:

| Stage | Who | What |
|---|---|---|
| 1. Parse | a form; or a model, into the same typed request | *"un kit de cocina para inducción por menos de 120 €"* → `KitRequest` |
| 2. Retrieve | the shop's search, as it is | up to 20 candidates a slot, in stock, already ranked |
| 3. Solve | **a pure function** | one product a slot, under the budget, with a trace |
| 4. Explain | code, from the trace; a model may reword it | *"the 24 cm pan, not the 28: 11.50 € were left and the 28 costs 14.90 more"* |

Invariant 12 applied to a recommendation: anything that can be decided
deterministically, is.

## Behaviour

### A kit is a template the shop wrote

A kit is not something a model invents from the request. It is **a template
the shopkeeper authored**: a code, a name in es and en, and up to five
**slots**. A slot is a search the shop already knows how to run — a category
and words, in both languages — and says whether the kit needs it.

```json
{
  "code": "KITCHEN_STARTER",
  "name": { "es": "Kit de cocina", "en": "Kitchen starter kit" },
  "slots": [
    { "code": "pan",      "category": "COOKWARE", "query": { "es": "sartén", "en": "frying pan" }, "required": true },
    { "code": "pot",      "category": "COOKWARE", "query": { "es": "cacerola", "en": "casserole" }, "required": true },
    { "code": "knife",    "category": "KNIVES",   "query": { "es": "cuchillo", "en": "knife" },     "required": true },
    { "code": "board",    "category": "COOKWARE", "query": { "es": "tabla de cortar", "en": "chopping board" }, "required": false },
    { "code": "utensils", "category": "COOKWARE", "query": { "es": "utensilios", "en": "utensils" }, "required": false }
  ]
}
```

`seed/kits.sample.json`, committed, read at start-up — as promotions and price
lists are. **Three templates** at laboratory scale: the kitchen starter, a
coffee corner (maker, grinder, kettle, cups) and a desk (lamp, chair, table,
storage). No table and no editor: a template has no writer yet, so it is not
state (ADR 0014's *a context with no writer is not a context yet*).

### The request

`KitRequest`: the template's code, a budget (an amount and its currency), the
culture, and **requirements** — facets the shopper asks every fitting product
to have, written as the search writes them (`INDUCTION:true`).

A requirement applies **only to the slots whose category declares that
attribute** (ADR 0056): *for induction* narrows the pan, the pot and the
coffee maker, and says nothing about a knife. That is decided by the category
tree, not by a model and not by the template's author.

### Retrieval

Each slot is one call to the search the storefront uses (`ISearchStrategy`,
hybrid where vectors are up and lexical where not — invariant 8 holds without
doing anything): the slot's words in the request's culture, its category, the
requirements that apply to it, in stock only, twenty at most. A product may be
a candidate of two slots and is chosen for one at most.

**A candidate's relevance is its rank, not its score.** Scores of two
different searches are not comparable; a rank is. Candidate *i* of *n* is
worth *(n − i) / n*, **in whole points of a thousand**: a sum of fractions is
not the same sum in every order, and a kit must not change with the order
its candidates arrived in.

### The solver

`IKitSolver.Solve(KitProblem) → KitSolution`, **pure**: no clock, no I/O, no
model, nothing but the kernel's `Money`. A problem is the budget and, per
slot, whether it is required and its candidates — code, price, relevance. It
lives under `Search/Kits/`, and an architecture rule holds that namespace to
the SharedKernel, which is what lets properties be stated about it.

**What it maximises, in this order**:

1. required slots filled;
2. optional slots filled;
3. the sum of relevance;
4. the lower total;
5. the candidates' codes, in order — so two kits as good as each other are
   always the same kit.

Branch and bound over the slots, exact: at five slots and twenty candidates it
is about four million leaves before any pruning (twenty candidates or nothing, five times), and the bound — the best
relevance still reachable with the money left — cuts nearly all of them:
77,693 nodes in the hard case, where the better ranked is always the dearer. **A
ceiling on nodes explored is part of the contract**: a solver that stopped at
its ceiling says so in the solution, and the answer is then *the best found*,
never passed off as the best there is.

**An unfillable kit is an answer, not an error.** When the required slots do
not fit the budget, the solution fills what it can in the order above and
names each slot left empty with its reason: *no candidate* (nothing in stock
matched), or *over budget by 14.90 €* (the cheapest one, and how much was
missing).

### The trace, and the explanation

The solution carries what the solver knows about its own answer, as data:

- per slot: the product chosen and its price; or why none;
- per slot: **the candidate it would have taken with more money** — the next
  better-ranked one — and by how much it did not fit;
- the total, the budget, and what was left;
- which constraint was binding: the budget, the stock, a requirement, or none;
- how many nodes were explored and whether the ceiling was reached.

The explanation is **written by code from that**, in es and en (invariant 6),
a sentence a fact (the amounts below are an illustration, not the seed's):

> *Kit de cocina · 118,40 € de 120 €.*
> *Sartén de hierro fundido de 26 cm, 49,90 €: la mejor valorada de las que
> sirven para inducción.*
> *Cuchillo de chef de 20 cm, 64,00 €. El juego de 3 cuchillos está mejor
> valorado y no cabía: cuesta 55,00 € más y quedaban 1,60 €.*
> *Sin tabla de cortar: la más barata cuesta 24,90 € y quedaban 1,60 €.*

That text is the feature's non-AI path and is always there.

*As built (K-3, 2026-10-13)*: `KitView.Explanation`, a list of sentences, each
about a slot or about the kit, so the page can put each beside what it is
about. `KitWords` is handed the kit and nothing else — not the search, the
template or the solver — so there is nothing it could say that the kit does
not carry, and a test holds every amount in it, in both languages and at
eight budgets, to the kit's own. As written:

> *Kit de cocina: 113.90 EUR de 120.00 EUR.*
> *Sartén: Sartén de hierro fundido de 26 cm, 49.90 EUR.*
> *Cuchillo: Cuchillo de chef de 20 cm, 64.00 EUR. Juego de 3 cuchillos está
> mejor valorado y no cabía: cuesta 55.00 EUR más y quedaban 6.10 EUR.*
> *Sin tabla de cortar: lo más barato cuesta 24.90 EUR y quedaban 6.10 EUR.*

Amounts are written as an order's total and the shipping terms are for the
same reader — two decimals and the currency's code — because the host runs
with invariant globalization; the page formats the kit's own numbers in the
reader's culture and shows these sentences as the shop said them. **It does
not say *the best ranked that fits induction***, as the illustration above
did: a slot's sentence names what it holds and what ranked higher, and a
claim about why a product ranked where it did is the search's to make, not
the kit's. A kit from a search that stopped at its ceiling says it is the
best found; one missing a slot it needs says it is not complete; a
requirement no slot could be held to is named.

**A model may reword it, and only reword it** (`P13-7`, not built yet — see
*Open* below): given the sentences,
it writes them as one paragraph in the page's language. Before it is shown,
code holds it to the trace as Tino's answers are held to their facts (ADR
0055): every amount and every product named in it must be one of the trace's,
and none of the trace's kit may be missing. One that fails is dropped and the
written-by-code text is shown; so is the case of no model at all. The
degradation is tested, not asserted.

### What the shopper pays

The solver adds **the prices the shop shows** — what a product's card says,
price list applied. The chosen kit is then quoted by Pricing as a cart of
those lines would be, and the page shows both: *118,40 €*, and under it *con
5 € en menaje: 113,40 €*.

The budget is held against the first number. Promotions only ever lower a
total, so a kit that fits before them fits after. What that gives up: a kit
that would only fit *because* of a promotion is not found. Said here rather
than discovered — making the promotion engine part of the bound would make the
solver depend on Pricing and end the properties.

### Surfaces

1. **The storefront's form, first** (`P13-8`): `/kits` lists the templates;
   one opens with its budget, the requirements its slots' categories declare
   as switches, and *Build*. The result is the shop's own product cards — one
   a slot, never a second kind of card — the total against the budget, the
   explanation, each empty slot with its reason, and **one clay action: add
   the kit to the cart**. A mock-up comes before the screen, as always.
2. **A sentence, second**: a box above the form. A model turns the sentence
   into the same `KitRequest` and the form is filled with what it understood,
   for the shopper to see and correct before anything is built. Which template
   is a closed question, asked in plain text (`ClosedQuestion`); the budget
   and the requirements are a small schema. No model, an unreadable answer or
   a sentence that names no kit: the form is there, empty, as it was.
3. **`build_kit` over MCP**, read-only and anonymous like the server's other
   tools (ADR 0039): the template, the budget, the requirements; the kit with
   its trace as structured data. An agent buying on somebody's behalf is the
   reader a budget constraint was made for, and it gets the arithmetic rather
   than a paragraph.

`GET /api/kits` (the templates, each with what can be required of it) and
`POST /api/kits/{template}/build` — the kit is in the address, the budget
and the requirements in the body — public, the second rate-limited at thirty
a minute a client; the OpenAPI document and the frontend's types
regenerated.

*As built (K-2, 2026-10-09)*: three templates in `seed/kits.sample.json` —
the kitchen starter, a coffee corner and a desk of three slots, not four:
the catalogue's storage is the kitchen's, and a slot that finds a spice rack
for a desk is worse than no slot. A file written wrong is refused whole and
by name. **The slice does not call Pricing**: Search sees Pricing through one
adapter and a rule holds that, so the kit comes back with each product's SKU
and whoever shows it asks for the quote — the page through the endpoint the
cart already uses, `build_kit` in the MCP project, which may dispatch both.
A requirement no slot of the kit can be held to comes back as unused rather
than being dropped in silence, and a category that declares nothing is not
held to any.

## Non-goals

- **A kit a model invents.** No template, no kit: the shop says so and offers
  the templates it has.
- **Quantities and "for two".** Every slot is one product. The catalogue has
  no *serves N* — it has pieces, cups and seats, each meaning something else —
  so a number of people would be a guess dressed as a constraint. See the
  questions.
- **An editor for templates in the backoffice.** A committed file until a
  shopkeeper needs to change one without a deployment; then it is a context
  with a writer, and a phase.
- **Compatibility between two products** — this lid fits that pan. Nothing in
  the catalogue says it. Requirements are of a product with a need
  (induction), which the catalogue does say.
- **Promotions inside the optimisation**, above.
- **Tino building kits.** A fourth surface over the same query, once the first
  three have been used.
- **Personalisation.** The same request gives the same kit to everybody; that
  is a property, and it is tested.

## Measures

- **Properties** (CsCheck, as the promotion engine's): the total never exceeds
  the budget; every required slot is filled or reported with its reason; no
  product twice; adding a candidate never makes the objective worse; the
  solution does not change when candidates or slots arrive in another order;
  the same problem is the same solution.
- **Exactness against an oracle**: for every generated problem small enough to
  enumerate, the solver's objective equals the best of all combinations. This
  is the property that says *branch and bound* is an optimisation and not a
  heuristic.
- **The trace is true**: for each *would have taken X with N more*, a property
  re-solves with the budget raised by N and finds X chosen.
- **The explanation says only the trace**: every amount in the generated text
  is in the trace, in both cultures; and the grounding of a reworded text is
  tested with texts that add an amount, change one, and drop a product.
- **Degradation**: no model — the form builds a kit and the written-by-code
  explanation is shown; no vectors — retrieval is lexical and a kit is still
  built.
- **The parse**: a fixture of sentences in es and en with the `KitRequest`
  each should become, run against the local model and recorded — measured, not
  gated, as Tino's suite is.
- **Nothing here touches ranking**, so the NDCG floors do not move; the gate
  runs anyway.
- **A browser spec**: build the kitchen kit from the form, see the total under
  the budget, add it to the cart, and find its lines there.

## Evidence of use

- `kit_built` — the template, the budget in bands, how many slots were filled
  and left empty, the binding constraint, and whether the request came from
  the form or a sentence. **Question**: are kits asked for, and can the shop
  fill them at the budgets people give? **Decision it moves**: a template
  mostly left with empty required slots at common budgets needs cheaper
  candidates in the catalogue, or to go.
- `kit_added_to_cart` — the template and how many lines. **Question**: is a
  solved kit worth buying? **Decision**: built often and added seldom means
  the choices are not trusted, and the explanation is what to work on before
  anything else.
- The server's `tendero.agent.tool_calls` already counts `build_kit` over MCP.

## Decisions taken with the author (2026-10-09)

All eight as proposed. Each is written with what it would have been
otherwise, because two of them go against the roadmap.

1. **Where it lives: in `Search`.** The roadmap said `Knowledge/Features/BuildKit`,
   and Knowledge cannot reference Search — it knows the model port and the
   kernel only. A kit is a constrained search: its candidates are search
   results and its relevance is rank. *Otherwise*: a project of its own,
   `Kits`, referencing Search and Pricing — cleaner to name, one more assembly
   that owns no state.
2. **Coverage before relevance.** The roadmap said *maximising relevance, then
   coverage*; this says the reverse. A kit that drops its pan to afford a
   better knife is not a kit. *Otherwise*: as the roadmap has it, and required
   slots become a hard constraint that can make the whole kit fail.
3. **Templates are the shop's, three of them, in a committed file.**
   *Otherwise*: a model proposes slots from the sentence — which is a model
   choosing, one step removed. *Later* (the author, 2026-10-14): the
   shopkeeper creates kits in the backoffice, which makes a template state
   with a writer. That is in the delivery plan's standing backlog.
4. **"For two" is left out, and said to be.** *Otherwise*: a slot may declare
   which attribute is per person (pieces of tableware, cups of a coffee
   maker, seats) and candidates below it are excluded — real for three or four
   slots of the catalogue, and it needs the candidates' numbers, which a
   search hit does not carry.
5. **The budget is held against shown prices, and the promotion is shown
   after.** *Otherwise*: against the quoted total, with the solver calling
   Pricing at each node — exact, slower, and no longer pure.
6. **A model rewords the explanation, behind a grounding check** — or not at
   all in this phase. The written-by-code text is complete without it; the
   rewording is where a sentence can go wrong and what the article's claim
   (*a model may phrase, never add facts*) needs to be demonstrated rather
   than stated.
7. **`build_kit` over MCP in this phase.** It is one registration over the
   same query. *Otherwise*: the storefront only, and the agent surface later.
8. **An ADR at the close**: *a recommendation is solved, not generated* —
   the four stages, the objective's order, the trace as the explanation's only
   source.

## Open

- **Where the rewording lives.** Search does not reference the model port
  (`AI`), and Assist may not reference Search. A reworder takes sentences and
  gives a paragraph, and the grounding check is code over strings — it needs
  no search at all. Three places it could go, the author's to choose with the
  mock-up: Search gains a reference to `AI`; a small slice in `Assist` that
  rewords any list of sentences it is handed, with Tino's grounding beside it;
  or it waits until the page shows whether the sentences need it.

## Building it, in order

1. **K-1 · The solver** (`P13-5`, `P13-6`): the types, branch and bound, the
   trace; the properties and the oracle first, red. The architecture rule for
   `Search/Kits`.
2. **K-2 · Templates and the slice**: the seed file and its reader, retrieval
   per slot, the requirement-by-category rule, the quote; the two endpoints,
   the OpenAPI document.
3. **K-3 · The explanation** (`P13-7`): written by code in es and en; then the
   rewording and its grounding, if decision 6 says so.
4. **K-4 · The mock-up**, for the author, before any screen.
5. **K-5 · The storefront's form** (`P13-8`), the page store, add to cart, the
   events, the browser spec.
   *As built (2026-10-14), on the wire of phase 13b — and **not yet seen in a
   browser***: `/kits` lists the templates and `/kits/:code` is one, both under
   `features/kits/`, with *Kits* in the header's row. The budget is a Signal
   Form's one field, read as a person types it (`59,90`) and sent as `Money`;
   the requirements are the template's own, as switches. The page store asks
   for the kit and then, through the call the cart makes, for what its lines
   come to with the shop's promotions — the solver's slice does not reach
   Pricing. **Each slot's sentence is composed by the page** from what the
   answer carries (`kit-sentences.ts`: the reason, the product, the amounts;
   decided on the mock-up), and the server's sentences stay for an agent.
   Adding the kit is one `add` a line through the shop's one cart, so what the
   cart refuses it refuses there, in its own words; the button says *the kit*,
   *this one* or *these N* by what there is to add. `kit_built` and
   `kit_added_to_cart` are in the tracking plan, the budget as a band. What
   the mock-up draws and this does not: an icon a slot — a row shows the
   product's own picture, or the shop's placeholder for an empty one. The
   browser spec (`kits.spec.ts`) is written and has not been run.
   **Seen by the author the same day**, and one thing was wrong: switching
   language emptied the page. The language is in the URL, so switching
   navigates from `/es/kits/…` to `/en/kits/…`, a page with a store of its
   own, and the kit was held only in the old one. Now what was asked lives
   in the address, as a listing's filters do
   (`?budget=59.90&needs=INDUCTION`). *Build* writes it there. A page that
   opens with an ask builds that kit in its own language, which also covers
   a reload, the back button and a link someone was sent. Requirements this
   kit cannot hold a product to are dropped from the ask. Only *Build* counts
   as `kit_built`, by the author's decision: a kit rebuilt from the address
   was already asked for.
   **Seen while trying it**: the same ask, sent twice within a minute, chose
   two different coffee makers. The first call ranked the Aroma Compacta first
   and every later call the 6-cup stovetop. The solver ranks by a slot's
   position in an ordinary search, so its answer is only as stable as that
   search. **Measured the same day.** Asked of Elasticsearch alone, with
   the lexical query's fields, the coffee-maker slot ranks the Aroma Compacta
   first in both languages. It is tied with the 6-cup stovetop (9.063 in es,
   15.812 in en), and the index's order breaks the tie. The warm hybrid search
   puts the 6-cup stovetop first and the Aroma Compacta fourth. So the first
   call, which chose the Aroma, ranked as lexical search does: its vector or
   reranking stage did not answer in time, and the search degraded as invariant
   8 asks. It was not caught in the act, because a degradation is on the span
   and on `tendero.search.degraded`, not in the answer. Two things follow. A
   kit is only as stable as the search's stages that answered, and lexical
   relevance has ties nothing breaks. **Decided by the author (2026-10-14): keep
   the hybrid ranking, warm it, break ties by code.** Built the same day:
   - `BuildKitHandler.TiesByCode` puts each run of equal scores in code order
     before the candidates are ranked. Only ties move, so a reranker's order,
     which need not follow the scores, stays its own. On this catalogue it also
     makes lexical and hybrid agree on the coffee maker: the stovetop's code
     comes first.
   - `WarmHybridSearchAtStartupWorker`: once the vector layer answers (it waits,
     every 5 s for two minutes), the API asks the hybrid search once in each
     language, as `WarmVisualSearchAtStartupWorker` does for pictures. Without
     the vector layer it does nothing, and a failure costs only the warming.
     It shortens the cold window and does not close it. A search that still
     degrades, because a model is down, ranks lexically by design (invariant
     8), and the tie-break keeps even that answer the same each time.
   - The kit's sentences now write money as the reader's language does
     (`MoneyText`: "113,90 €", "€113.90"). They wrote "113.90 EUR" to everyone.
     At the author's word, so do the other sentences the server writes for a
     reader: the shipping terms and an order's total, which Tino cites, and a
     promotion's unmet minimum. Tino's suite, run once against it, gave 28 of 30,
     its best before (`docs/analysis/tino-models.md`).
6. **K-6 · A sentence**: the parse, its fixture, the form filled from it.
   *As built (2026-10-14), option A of `docs/plans/2026-10-14-kit-k6-a-sentence.md`*.
   The slice `Assist/Kits/UnderstandKitSentence` reads the shop's kits through
   `list_kits`, a read Search provides (ADR 0054). It has a new audience,
   `ToolAudience.Page`: a read a slice makes for a page and that no agent is
   handed, because Tino takes every read of its own audience and would have
   changed unmeasured. Two departures from the plan, both on the side of
   invariant 12:
   - **The model chooses only the kit.** It is one closed question in plain
     text, among the kits' names and *none of these*. **Code reads the budget
     and the requirements**, because they are written in the sentence: an
     amount counts when a currency is beside it or a price cue comes before it
     ("menos de", "hasta", "under"…), so "para 4 personas" is not a budget,
     and a requirement counts when its label is in the sentence as a word,
     accents aside. That makes one call to the model instead of two, and
     nothing a reader could read with certainty is left to it.
   - **The box is on the kits' list, and the sentence builds.** Since K-5's
     fix, an ask lives in the address. So the box sends the shopper to
     `/kits/CODE?budget=…&needs=…`, and the page builds that kit with the form
     showing what was understood, for the shopper to correct and build again.
     The plan had the form filled and nothing built. The navigation's state,
     not the address, marks it as the sentence's, so that one `kit_built`
     counts with `source: sentence` and a reload counts nothing.
   It runs on the storefront's model, Tino's profile. With Tino off, no
   profile, or a model that fails, the answer is `understood: false`, never a
   refusal. `GET /api/kits/understand/status` (configured, not probed) says
   whether to draw the box at all, and with no model the list is the page it
   was. `kit_sentence_read` (understood or not, which kit, whether a budget
   was said) answers whether a sentence gets a shopper to a kit. The browser
   spec scripts the model's answer (`tino.script.json`, the step *put a kit
   together*).
   **Measured (2026-10-14)**, `node tools/kit-sentences/run.mjs` against the
   running stack: 14 sentences (8 es, 6 en; three name no kit), local
   `qwen3.5-4b-q8_0`, **13 of 14 right whole** (kit, budget and
   requirements), each in 131 to 367 ms. The one miss is *"a kitchen kit for
   induction under €200"*, which the model answers *none of these*. It does the
   same without the budget, and without "induction", so it is the model not
   hearing "kitchen kit" as "Kitchen starter kit". It is the browser spec's
   sentence, which is scripted there. One wording was tried against it: listing
   what each kit is made of. That came out **worse, 12 of 14**, and also lost
   *"everything I need to start cooking"*, so the question stays as it is. Code
   read every budget and requirement right in the 11 sentences where a kit was
   named.
7. **K-7 · `build_kit`** over MCP, if decision 7 says so.
   *As built (2026-10-14)*: two tools in `Mcp/Tools/TenderoTools.cs`, not
   one. `build_kit` takes a template's code, so an agent needs to learn the
   codes, and `list_kits` gives them, with each kit's slots and its
   requirements as `CODE:VALUE`. Both are the REST endpoints' own queries,
   read-only and anonymous like the server's other reads, and annotated as
   such. The budget is in **minor units**, as the wire answers prices and as
   `search_products` takes them. The answer is the page's `KitView`: total,
   what is left, what held the kit back, every slot with its product or its
   gap, and the shop's sentences. A template the shop does not have is the
   problem `KitNotFound` (404), naming it in the language asked and pointing
   to `list_kits`. Pricing the kit at the basket stays where it is on the
   page: a second answer, here the checkout's. The server's
   `tendero.agent.tool_calls` counts both by name, with nothing added.
8. **K-8 · Close**: the ADR, Stryker over what the phase touched, the
   captures, the notebook and the book's material map.
