# Spec: Steward, the shopkeeper's copilot

**Status**: written 2026-10-01 from the author's decisions in the session that
designed it, for the author's review before a plan is written. Phase 12 of
`docs/delivery-plan.md`.
**Slice(s)**: a new context, `Assist` (the proposals); Catalog (`SetProductTranslation`,
a gaps query); Ordering, Inventory and Catalog for the reads; Knowledge (the bulk
review of claims); SharedKernel (the audit's `ProposalId`); a shared model port;
the backoffice (the panel, the review, the Proposals list)
**Article**: *My copilot doesn't write text, it writes commands* — why the answer
ends in a button. A second one on the measurement: local against hosted, with
accuracy, latency and cost.

## Problem

Tendero's backoffice answers every question a screen was built for, and none
that spans two. "Which products sell little and come back a lot?" is three
screens and a spreadsheet. "Translate the 12 products with no English name" is
twelve forms. "What is my catalogue missing?" needs knowing every screen that
could say. Differentiator 4 of the vision is a copilot over the shop's own data
that **proposes approvable actions**; this is it, for the shopkeeper.

## The model: what the references do

| Product | What it does | What this spec keeps |
|---|---|---|
| **Shopify Sidekick** | a panel in the admin that answers about the shop and drafts changes the merchant confirms | a side panel on every screen, aware of the screen; changes confirmed by a person |
| **Amazon's seller assistant (Project Amelia)** | answers about a seller's sales and account in natural language | questions across reports, in words |
| **Seller Central's "Restock Inventory"** | a recommendation with its arithmetic | a proposal carries its numbers and its reason |

What none of them shows, and Tendero does: **every proposal is a typed command
with its provenance, every approval is an audited action of a named person, and
every conversation shows what it cost.**

## Decisions taken with the author (2026-10-01)

1. **Models behind one port, measured before choosing — and the measurement
   last.** `IChatClient` (Microsoft.Extensions.AI) is the interface every
   provider meets; providers are profiles in configuration, all spoken through
   the OpenAI-compatible protocol (the in-process ONNX client is a second kind).
   Everything is built and proven on the **local model first**; the hosted
   profiles (Gemini, DeepSeek or another cheap one) are measured only once it
   works, because their tokens are paid. Microsoft's MAI models were looked at
   and left out.
2. **Who it serves: the shopkeeper, about the shop (A).** The operator's half —
   audit, outbox, search quality, the AI's own cost — is a future feature (B).
3. **Where: a side panel on every screen**, opened from the header and the
   palette, aware of the screen; a sheet on a phone; and a **Proposals** list of
   work, so nothing proposed is lost when the panel closes.
4. **Bulk approval with a review that cannot be skipped.** "Approve all" first
   shows every row — now, proposed, provenance — and any row can be unticked;
   each approved row is its own command and its own audit row.
5. **The same review for claims**, amending R-2's rejection of bulk approval:
   one review component for everything a model proposes, and a rule per kind —
   a claim with weak support (`isWeak`, decided on the server) is never
   approved in bulk.
6. **Conversations are not kept.** What stays is one row per model call (cost,
   latency) and each proposal with its provenance.
7. **The command is `SetProductTranslation`**, not `SetProductName`: it writes a
   product's text in one language and leaves the original alone. The
   shopkeeper never reads a type name — *Translate a product* / *Traducir un
   producto*, *English name* / *Nombre en inglés*.
8. **`Assist` becomes the seventh context.** CLAUDE.md said `Assist` owns no
   state; proposals have a lifecycle, a writer and actors of their own, which is
   how a context earns its boundary (ADR 0014). An ADR records it.
9. **An approval runs the command in the same request, as the person approving.**
   Not through the outbox, as claims do: the command is the action itself, the
   audit row must name the approver, and the review shows each row's result at
   once.
10. **Two futures recorded, not built**: suppliers and purchase orders
    (`Purchasing` — Inventory has no "order from the supplier"), and
    notifications. Both in the backlog, to be analysed when their time comes.

## What Steward is for

Without it the shopkeeper **could not, or not at a cost worth paying**:

1. ask questions that cross screens — sales, returns and stock together;
2. do content work in bulk — a hundred English names, each reviewed;
3. find what the catalogue lacks without knowing where to look;
4. get a decision as a list of proposals, each with its reason and numbers.

It **eases**: a summary of the week against the last; finding a product by what
is remembered of it; questions about the screen in front of you; reviewing
twelve changes in one step without losing any; a trace of everything the AI did.

Later, each as its own decision: descriptions from approved claims, purchase
orders, campaigns, explaining a dashboard's change, the operator's questions (B),
alerts without being asked (with R-10). The v1's real value is points 1–4; the
rest is comfort, and `proposals_reviewed` will say whether the proposals are
worth reviewing.

**Never**: change anything without a person's approval; see a customer's
personal data; approve a claim or another proposal (no AI approves what an AI
proposed); decide what code can decide — the model parses and phrases, the
contexts compute the figures and apply the rules (invariant 12).

## Behaviour

### The panel

- Opens from **Steward** in the header, or *Ask Steward* in the palette (Ctrl K).
  420 px beside the content from 1024 px; a full-screen sheet below. *As built,
  and changed on 2026-10-06 by the author*: docked from 85 rem, the panel stands
  beside the whole frame — the header and the page's footer included, the window's
  height — as Tino's stands beside the shop; the header ends at its edge. Until
  then it sat under a header that spanned the window. **Steward answers in the
  language of the shopkeeper's screen** — the one chosen in the menu — whatever
  language they write in (the same day, the rule Tino took). The instructions say
  it, and code holds it, because the local model does not: an answer plainly in
  another language is said again by the same profile, and shown only if it is then
  in the screen's language with the same figures (`docs/analysis/steward-models.md`).
  Prompt `steward-2026-10-06b`.
- Its head names the **screen context** it was opened on (*On: Products · Active*,
  a product's page, an order). The context is sent with each question, so
  "translate this one" needs no code.
- Each answer shows, above its text, **what it read** (*Read catalogue gaps ·
  12 rows*) — every tool call, by name and row count.
- Proposals arrive as a **card**: kind and count (*12 proposals · English name*),
  the reason, three rows of *now → proposed* and *and 9 more*, **Review 12
  proposals** (the view's one clay action), and *Kept in Proposals*.
- Under each answer, the **cost line**: *This conversation: 3 calls · 1,842
  tokens · €0.0021 · 4.1 s · \<model\>* — the sum of its `AiInvocations` rows.
- The composer, and under it the promise: *Steward reads and proposes. Nothing
  changes until you approve.*
- While a turn runs, the panel says what it is doing (*Reading the catalogue…*);
  a turn can be cancelled.
- Near the context limit the panel says so and offers a new conversation; it
  never trims the history in silence.

### The review

A dialog over the screen: every proposal of the batch as a row — code, *now*,
*proposed*, a checkbox ticked by default — with the provenance in its head
(model, prompt version, time). Its foot: *11 of 12 selected*, **Cancel**,
**Reject the unticked one(s)**, **Approve 11**. After approving, each row shows
its result in place: applied, failed with its reason, or stale.

### The Proposals list (`/steward/proposals`)

A list of work on the shared pattern (`worklist/`: chips, pane, pager, URL):
chips *To review · Applied · Rejected · Failed · All*, counted on the server;
rows with product, action (*Add the English name*), proposed value and when.
The pane: the change (*now → proposed*), the reason, the provenance (proposed
by, prompt, asked by, command, trace), **Approve** and **Reject…** with a
reason. The rail gains *Steward → Proposals* with a badge of those to review.

### When no model answers

The panel says that no model answered (the active profile and its fallback both
tried), that nothing was proposed and nothing changed, and offers **the same
answer without AI** where one exists — *Products missing an English name* is a
filter on Products — and the Proposals list. *Try again* in the composer. The
rest of the backoffice is untouched. This is invariant 8 for Steward, and it is
tested, not asserted.

### The bulk review of claims

The review component, on a product's claims: attribute, proposed value and the
**evidence quote** per row. A weakly supported claim is shown unticked and
disabled — *Weak support — open it to decide* — and the foot offers *Open the
weak one* beside *Approve 5*. The server refuses a weak claim in a bulk
approval, whatever the client sends.

## The models

**One port, `IChatClient`.** The factory that builds an OpenAI-compatible client
(`OpenAICompatibleChat`, in Knowledge today) and the ONNX client move to a
shared place, used by Knowledge, Steward and later Tino (12b).

**Profiles in configuration**, each registered as a keyed service — a new
provider is an entry, not a class:

```
Steward:Models:<name>
  Kind           OpenAICompatible | OnnxInProcess
  Endpoint       llama.cpp, https://generativelanguage.googleapis.com/v1beta/openai/, https://api.deepseek.com, …
  Model          the model id the endpoint expects
  Auth           None | Bearer
  ApiKey         an Aspire secret parameter, never in the repository
  InputPrice     € per million input tokens  (0 for local)
  OutputPrice    € per million output tokens
  ContextTokens  the window the profile is driven against
Steward:Model     the profile that answers
Steward:Fallback  the one tried when it does not
```

**The pipeline**, the same for every profile: function invocation → OpenTelemetry
(the GenAI conventions) → **a metering client** that records tokens, cost and
latency into a channel, drained by a worker into `AiInvocations` without holding
the request → the model.

**The local model gets its own container**, the same GGUF as the claim
extractor (Qwen3.5-4B Q8, ADR 0031), with its own slot: the extractor's one
4,096-token slot is taken, and tool definitions alone are 1,000–1,500 tokens.
**8,192 tokens is an estimate, not a measurement**: a typical turn is about
2,700–4,700 (system ~700, six tools ~1,300, context and question ~150, tool
results 500–1,500, the answer 400–800). The window is fixed after the build from
the largest conversation the evaluation records, plus a margin. It starts with
`Tendero__Steward=true`, explicit-start otherwise, sized from a measurement like
every container.

**The measurement comes last** (step S-7): the `copilot` cases against the local
4B, a small model (Qwen3-1.7B, or Phi-4-mini in ONNX — the weakest at claims in
phase 10, kept as the curve's small end), and the hosted profiles the author
chooses (Gemini, DeepSeek). Per profile: right tool, valid arguments, exact
proposals, latency p50/p95, tokens and € per conversation. Results in
`docs/analysis/steward-models.md`; the author picks the default with them.

**The agent** is Microsoft Agent Framework (invariant 9, ADR 0005): short-lived,
one per turn, its tools the closed set below. **The conversation travels with
the client**: each turn posts the messages so far and the screen context, the
server keeps nothing between turns. A turn is capped — at most six model calls,
and a token budget per conversation — so a loop costs a bounded amount.

## What Steward reads

Tools are thin wrappers over `IQuery<T>` handlers the contexts own, dispatched
**as the person asking**, so the endpoints' policies apply as on a screen. No
free SQL, no traces, no business logic in `Assist`.

| Tool | Answers | From |
|---|---|---|
| `find_products(text)` | "the blue stoneware mug" → code and name | Catalog, lexical search (no vectors needed) |
| `catalogue_gaps(kind, culture?)` | products missing a translation, an image, a required attribute; drafts waiting | Catalog — **a new query** |
| `sales_summary(period)` | sales, orders, average order, mix by category, against the period before | Ordering, R-9's figures |
| `product_performance(period, best\|worst, category?)` | products that sell most and least, units and revenue | Ordering, the best sellers' ranking rule |
| `stock_to_restock(warehouse?)` | what needs restocking, with its coverage in days | Inventory, R-7.1's restocking rule |
| `returns_summary(period)` | returns by reason and by product | Ordering |

*As built in S-3a (2026-10-02)* — the query behind each tool, every one capped at
50 rows with its total and shopkeeper-only over HTTP:

| Tool | Query | Notes |
|---|---|---|
| `find_products` | Catalog `ListProductsQuery` (`Text`) | the catalogue, every status; not Search, which is the shopper's (anonymous, hybrid, Active only) |
| `catalogue_gaps` | Catalog `CatalogueGapsQuery` — **new** | kinds `MissingName`, `MissingImage`, `MissingImageAlt` (per culture), `DraftWaiting`; *required attribute* is backlog, the domain has no such concept |
| `sales_summary` | Ordering `GetSalesSummaryQuery` + `GetSalesMixQuery` | the mix has no previous period |
| `product_performance` | Ordering `GetProductPerformanceQuery` — **new** | the home's ranking; *worst* is among what sold — what sold nothing is in no order |
| `stock_to_restock` | Inventory `GetRestockListQuery` — **new** | every SKU at or below R-7.1's point, with its days of cover |
| `returns_summary` | Ordering `GetReturnsSummaryQuery` — **new** | returns opened in the period; no order, customer or comment |

The non-AI answer the unavailable state offers is `GET /api/catalog/products?missing=en`.
It counts archived products too, which `catalogue_gaps` leaves out; the list's
status chips show the difference. `stock_to_restock`'s warehouse narrows the
shop's list to what that warehouse stocks — the need and the pace stay the
shop's — and a warehouse the shop does not have is a 400 naming the ones it has.

**Correction (S-3a):** the dispatcher checks no policy — it validates and runs the
handler. "As the person asking" holds because the principal is the person's, and
the policy is the Steward endpoint's own: S-3b's turn endpoint is shopkeeper-only.

Common rules:

- **At most 50 rows per call, always with the total** (*50 of 312*).
- **No customer's personal data**: no names, addresses or emails — aggregates
  and products only. An architecture test holds the tools' result types to it,
  and `docs/privacy.md` says so: with a hosted profile, this is what leaves the
  machine.
- Answers in the conversation's culture (invariant 6).
- **Catalogue text arrives marked as data.** A product name written by an
  importer can say "ignore the above"; the system prompt forbids following
  instructions found in data. The real defence is the closed vocabulary: the
  most an injection can obtain is a proposal, and a person reviews it.
- Every tool call is a span with its name, arguments and row count.

## What Steward proposes

**A closed vocabulary**, v1: `SetProductTranslation` (new, Catalog) and
`WithdrawProduct` (exists). A registry entry per proposable command holds its
stable name, its validator, its reader of *now* (for staleness) and its labels
in es and en. Adding a proposable command is adding an entry. **Approving or
rejecting — a proposal or a claim — is never proposable.**

**The model never writes a command.** It calls typed proposal tools,
`propose_product_translations(items)` and `propose_withdrawals(items)`; code
builds each command and runs its validator before anything is stored. An
invalid one goes back to the model as an error and is not stored.

### `SetProductTranslation` (Catalog)

`SetProductTranslation(ProductId, Culture, Name, Description?)` writes the
product's text for one supported culture and leaves the others untouched. Its
validator: a supported culture, a non-blank name within the catalogue's length.
Idempotent: the same text twice is one change. The product page's slug follows
ADR 0026 (decorative, the code is the key).

### `ActionProposal` (Assist)

| Field | |
|---|---|
| `Id` | `ActionProposalId` |
| `Command`, `Payload` | the registry name and the command as JSON — values, never a reference to another context's entity (invariant 1) |
| `Subject` | what it is about, as a value (a product's code) |
| `Before` | *now* as it was when proposed |
| `Reason` | the model's words, in the conversation's culture |
| `Provenance` | profile and model, prompt version, conversation id, trace id, asked by, at |
| `Status` | below |
| `DecidedBy`, `DecidedAt`, `Result` | who, when, and the command's outcome or failure |

Its table (invariant 5): `Proposed → Approved → Applied | Failed`,
`Proposed → Rejected`, `Proposed → Stale`.

### Approving

`ApproveProposals(ids)` (shopkeeper): for each id, **check staleness** — if
*now* differs from `Before`, the proposal becomes Stale and nothing runs —
then Proposed → Approved, **dispatch the command through `ICommandDispatcher`
as the approver**, and record Applied or Failed with its reason. Each row
answers on its own; one failure stops none of the others. The audit row of each
command carries the new **`ProposalId`** column, which is what lets the audit
say *proposed by Steward, approved by Lucía*. If the process dies between
Approved and the result, the list shows *Approved, result unknown* and it can be
retried safely: the command is idempotent and the staleness check runs again.

`RejectProposals(ids, reason)` records the reason; the reason is what a prompt
change is judged against.

## AI observability

- **`AiInvocations`**: one row per model call, Steward's and the claim
  extractor's — trace, conversation, operation (`steward.turn`,
  `knowledge.extract`), profile and model, prompt version, input and output
  tokens, **cost in micro-euros at the profile's price at that moment**, latency,
  outcome (ok, error, timeout, fallback answered).
- **The cost line** of a conversation is the sum of its rows.
- **A Grafana dashboard**, `ai-usage.json`, beside `shop-pulse`: cost per day and
  per model, latency p50/p95, tokens per model, fallbacks and unavailability.
  The backoffice's own AI panel (`P12-7`) moves to feature B.

## Evidence of use

ADR 0036: each event with its question and the decision its value changes. None
carries the question's text.

| Event | Question | Decision it changes |
|---|---|---|
| `steward_opened` (screen) | is Steward used, and from where? | if hardly opened, where it lives and how it is reached |
| `steward_answered` (tools, model calls, cost, profile) | what is it asked, and what does it cost? | which tools to add or drop; the default profile |
| `proposals_reviewed` (proposed, approved, rejected, unticked) | **are its proposals worth reviewing?** | above 30 % rejected, the prompt or the model is the problem, not the screen |
| `steward_unavailable` (profile tried) | how often does no model answer? | the default and the fallback profile |

## Evaluation and tests

- **The `copilot` suite** (`tools/SearchEval`, an `IEvaluationSuite`): 20–30
  cases, each a question, a screen context and a fixed laboratory state, with
  the **expected tool calls** (name and key arguments) and the **expected
  proposals** (command and payload). No judge model: the answer is a command,
  and a command is compared exactly. It reports right tool, valid arguments,
  exact proposals, latency, tokens and cost per profile — the same run is S-7's
  measurement. Like the claims gate, the model-backed run is local and at phase
  close: a CI runner cannot hold a 4B model.
- **A `Scripted` profile kind**, for tests only: a deterministic `IChatClient`
  that answers from a script. It runs in CI the whole pipeline — agent, tools,
  proposals, approval, audit — and the browser spec: panel → card → review →
  approve → the audit row naming the proposal.
- **Unit**: the registry, both validators, `ActionProposal`'s table, staleness,
  the approval loop with one row failing, the tools' 50-row cap and total, the
  turn's caps, cost arithmetic.
- **Contract**: a shared suite every chat profile kind passes (a tool call round
  trip, usage reported, a timeout surfaced as such) — `Scripted` and llama.cpp
  in CI where it can, the hosted ones at S-7.
- **Architecture**: `Assist.Domain` references SharedKernel only; the tools'
  result types carry no customer field; no proposal tool for approving.
- **Degradation, tested**: the active profile down → the fallback answers; both
  down → the turn answers *unavailable*, the panel shows its state with the
  non-AI links, nothing is stored.
- **Mutation**: a hand mutation per test written for existing code; Stryker over
  `Assist`, Catalog and Knowledge at the phase's close.

## Security

- The endpoints are shopkeeper-only (`/api/steward/*`), nothing anonymous.
- Tools run as the person asking; a tool can read nothing the person could not.
- A turn's caps (model calls, tokens) bound what a loop or an abuse costs; a
  rate limit per person on turns.
- The history the client posts is the person's own: forging it can at most
  produce proposals the same person then reviews.
- Hosted profiles receive aggregates and catalogue text only (above), and their
  keys are secret parameters.

## Non-goals

The operator's questions (feature B) and the backoffice AI panel · kept
conversations · streaming answers (the panel shows progress instead; revisited
after S-7's latencies) · Steward composing dashboards (a separate product idea,
2026-09-27) · any action without a person · purchase orders (`Purchasing`) ·
notifications and alerts.

## Building it, in order

1. **S-1 · Proposals without a model.** `SetProductTranslation` in Catalog; the
   `Assist` context with `ActionProposal`, the registry, approve and reject; the
   audit's `ProposalId`; the ADR that makes `Assist` a context, and CLAUDE.md.
   *As built (2026-10-02)*: each proposable command is an `IProposableCommand`
   its own context registers (`ProposableCommand<TPayload, TCommand, TResult>`),
   so `Assist` references the kernel alone (ADR 0052). Rulings the spec left
   open: an `Approved` proposal retried after a crash skips the staleness check;
   "now" is a JSON snapshot of the fields the command would change, compared by
   value because jsonb keeps neither spacing nor key order; the Proposals list's
   query moves to S-4; names up to 200 characters and descriptions up to 4,000;
   batches of at most 100; each approved row runs in a DI scope of its own.
2. **S-2 · The model port.** The shared factory, profiles and keyed registration,
   the metering client, `AiInvocations` and its worker; the `steward-model`
   container behind `Tendero__Steward`.
   *As built (2026-10-02)*: the shared place is `ElGuerre.Tendero.AI` (ADR 0053),
   not a context. Rulings the spec left open: a profile has a `TimeoutSeconds`
   (480 for the local one: the typical turn is up to ~458 s on the CPU, most of
   it writing the answer — **S-3 caps Steward's output tokens**); its endpoint is
   the provider's base URL with `/v1`; the client makes one request per call (the
   SDK's three retries are off); a full metering log drops the newest row and
   counts it; the table is `telemetry.ai_invocations`. `steward-model` peaked at
   4.81 GiB with a full window and is limited to 5.5 GB
   (`docs/analysis/steward-model-sizing.md`).
3. **S-3 · Steward reads and proposes.** `catalogue_gaps`, the six tools, the two
   proposal tools, the agent, `POST /api/steward/turns`, the caps, the fallback,
   the `Scripted` kind.
   *As built (2026-10-02, split into S-3a and S-3b)*: the reads are tools their
   contexts own (`IReadTool`, ADR 0054) — `find_products`, `catalogue_gaps`,
   `sales_summary`, `product_performance`, `stock_to_restock`, `returns_summary` —
   answered in a `{ data, rows, total }` envelope; the proposal tools are generated,
   one per proposable command: `propose_set_product_translation` and
   `propose_withdraw_product`. A turn is a `ChatClientAgent` (Microsoft Agent
   Framework 1.23.0) with five tool rounds at most (six model calls), 600 output
   tokens per call, a conversation budget of 60,000 tokens read from
   `telemetry.ai_invocations`, and twenty turns a minute per person. The fallback
   answers when the model fails, except after a proposal was stored (a second
   model would propose it again). The `Scripted` kind can loop and can fail after
   its tools, so both are tested.
   *Corrected after the final review (2026-10-02)*: what a call takes is counted
   with the instructions and the tools' schemas in it, plus the answer's 600
   tokens; a profile whose window cannot hold that is not asked, and when none can,
   the turn answers `ConversationTooLong` so the panel offers a new conversation.
   A model's wrong arguments are the tool's answer, with the reason, never a fault
   — so a model that keeps getting them wrong answers at the cap rather than
   falling back. Any other tool failure reaches the model as a bare error (no
   exception text leaves the shop), and only a *model's* failure — the network, a
   timeout, a provider's status, the runtime — is worth the fallback; a failure of
   the shop's own is `Unavailable` at once and logged as an error. A whole turn has
   ten minutes (`Steward:TurnSeconds`), with no fallback after it. The screen is
   one line of at most 100 characters, quoted and marked as data in the
   instructions.
   **The budget bounds a conversation, not a person.** It is checked with the
   turn's own first call in it, but the conversation id is the client's to send,
   so a person who starts new conversations starts new budgets; what bounds a
   person is the rate limit, twenty turns a minute. While every profile is local
   that costs electricity, not money. **Before a paid profile is configured (S-7),
   a budget per person per day is required**: the metering rows carry no person
   today, and adding one is a migration S-7 owns. The turn's cost line counts the
   answering profile only — a failed attempt before a fallback is in the rows,
   and S-6's cost per conversation reads the rows, not the line.
4. **S-4 · The backoffice.** The panel and its sheet, the screen context, the
   card, the review, the Proposals list and its rail entry, the unavailable
   state, es and en.
   *As built (2026-10-02)*: a proposal stores, when it is made, a **display**
   its owning context writes (`ProposalDisplay`: the subject's title, the kind of
   change in words, each field now and proposed), so the card, the review and the
   list show a product's name without Assist reading Catalog; a proposal made
   before has none and reads by its code. `GET /api/steward/proposals` serves the
   list (five filters counted on the server — To review holds an approval whose
   result was never recorded, Failed holds the stale) and a turn's ids, in order.
   The conversation is a root store that survives the panel closing; whether the
   panel is open is a store of its own, so the panel's code is a deferred chunk.
   The screen context is the route's title and the open row, or else the filter.
   The way on without AI is a new filter on Products, `?missing=en`. Rulings the
   spec left open: no "Reading the catalogue…" per tool (turns do not stream — a
   non-goal), one working line with Stop instead; the unavailable panel offers
   the missing-English filter whatever was asked, since a question cannot be
   mapped to a filter deterministically; the review asks for a reason before
   rejecting the unticked; the Proposals screen joins `backoffice_view_opened`
   with S-6's events. Seen in the browser against a Scripted profile: four
   names proposed, three approved and one rejected with its reason, each row's
   result in place, the unavailable state — which found that a lone profile's
   failure was reported as a fallback's too, fixed.
   *Corrected after the final review (2026-10-02)*: below 1024 px the sheet is a
   modal `<dialog>` (the page under it inert, Esc closes it, the focus returns);
   the card offers Review only for what still waits — after its own review it
   offers to see the batch, quietly; a change's values are words in both
   languages (a withdrawal reads "A la venta → Borrador", never the enum's
   name), and a display stored with plain text still reads; the rail's badge is
   read again when an answer brings proposals; one live region, the newest
   answer's. Rulings: **closing the panel does not stop a turn** — the
   conversation is a root store, the turn runs on and its answer is there when
   the panel opens again (Stop is the way to stop one, and what a turn proposed
   is kept either way); the unavailable notice says "the model and its fallback
   both tried" also when the first was skipped for its window rather than
   asked, a rare case and a wording one.
5. **S-5 · Claims in bulk.** The review component on claims, the server's refusal
   of weak claims in bulk, R-2's amendment in `docs/specs/redesign.md`.
   *As built (2026-10-02)*: `POST /api/knowledge/claims/approve` approves a
   product's claims, each through the one-claim command in a DI scope of its own
   (its own audit row), and answers WeakSupport on the row of a weak one — the
   server's `IsWeaklySupported`, never the screen's. `GET /api/knowledge/claims`
   takes `?product=`. In the backoffice the review's frame is one component,
   `worklist/review-dialog`, which Steward's proposals review and the claims review
   both project into; the claims review opens from the claim pane, quietly.
   Rulings the spec left open: there is no "reject the unticked" for claims (the
   mock-up has none, and a claim is rejected on its own, from the queue); the
   attribute is shown by its code, as the queue's row does.
   *Corrected after the final review (2026-10-02)*: every row of a bulk approval
   goes through the one-claim command, refusals included, so a weak claim pushed
   in bulk is a **denial on the audit log** (WeakSupport), not a silence; *Open the
   weak one* opens the queue narrowed to that product (`?product=`), where the claim
   is on the first page — in the whole shop's weak queue it was only if it was
   among the shop's twenty weakest; the weak rows come last, as the mock-up draws
   them; a product past a hundred claims says how many the review shows; a failed
   approval says so. **The event that proves it is used** is
   `proposals_reviewed` with a `kind` of `claims` beside `proposals` (proposed,
   approved, unticked) — it answers whether a batch is reviewed or rubber-stamped,
   R-2's fear, and S-6 adds it to the tracking plan with the other three. Ruling:
   the pane offers the bulk review even when the product has one claim; knowing
   that would cost a read per opened claim, and the review of one is harmless.
6. **S-6 · Measured and evaluated.** The `copilot` suite on the local model,
   `ai-usage.json`, the four events in the tracking plan, the window fixed from
   the largest conversation.
   *As built (2026-10-03)*: metrics `tendero.ai.calls`, `tendero.ai.cost` and
   `tendero.steward.turns` and the `ai-usage.json` dashboard beside `shop-pulse`;
   the four events, `proposals_reviewed` with a `kind` of proposals or claims; the
   `copilot` suite in `tools/SearchEval`, composed in-process over a disposable
   Postgres, 24 cases (26 since P12-10); the Scripted model in rounds with `{from:tool:path}`; the
   browser spec, in CI, over `Tendero__StewardScript`; the audit links a row to its
   proposal. **Measured** (`docs/analysis/steward-models.md`): the local 4B passes
   22 of 24, p50 22.5 s, the largest call 5,996 tokens — the window stays 8,192,
   now measured; the container's limit rises to 6 GB (peak 5.315 GiB) and its
   prompt cache is off (`--cache-ram 0`; it had the container killed). The suite's
   first real turns found two schema bugs every real model would have hit.
7. **S-7 · The measurement.** The hosted profiles with the author's keys, the
   small model; `docs/analysis/steward-models.md`; the author chooses the
   default. **First, before any paid profile exists**: a token budget per person
   per day, with the person on the metering row (S-3b's correction above).
   *Budget as built (2026-10-03)*: the row carries the person's subject; a turn
   is refused with `DailyBudgetExceeded` when the person's tokens since the shop's
   midnight (ADR 0046) plus this turn's first call pass
   `Steward:PersonDailyTokenBudget` (300,000 — about seventy local turns), in any
   conversation they started. A turn is checked before it is asked, so the last
   turn of the day may overshoot by its own calls, never by a second turn.
8. **S-8 · Close.** ADR 0019 (*the copilot proposes commands, never prose*),
   Stryker, the before and after captures, the notebook and the material map.

## The mock-ups (2026-10-01)

`design/unpublished/mockups/backoffice-steward.pen`, in English, with its PNGs in
`backoffice-steward/`: the panel at 1440, the review, the Proposals list with its
pane, the sheet at 390, the unavailable state, and the bulk review of claims. The
model shown (`gemini-2.5-flash`) is an example; S-7 decides.
