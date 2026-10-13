# Spec: the wire — one contract, read by a front end and by an agent

**Status**: written 2026-10-13, on the author's four decisions of the same day
(*Decisions taken with the author*). **Accepted in scope; this text waits for
his reading before W-1 starts.** Phase 13b of `docs/delivery-plan.md`: after
the kit solver's K-4 and before its page (K-5), so the page is built once.
**Slice(s)**: the SharedKernel (the wire's types), every slice that answers
with money or refuses with a sentence, `Mcp` (the shop's own tools), `Ucp` (its
mappers get thinner), both front ends, the traffic simulator and the
evaluation tools.
**Analysis**: `docs/analysis/agent-facing-api-conventions.md` — what MCP, UCP,
ACP and WebMCP say, and the audit this phase answers.
**Article**: *An API with two readers*; and *Money is not a decimal*.

## Problem

Tendero's own API was written for Tendero's own front ends, and reads like it:
an amount is a JSON number with a fraction, usually with no currency beside it;
a refusal is an English sentence in a string. The front ends know which
currency and what to do on a 409 because the same hands wrote both sides.

An agent does not. The protocols written for agents in 2025–2026 — MCP, UCP,
ACP — converge on a contract with two readers: data a machine can act on, and
the merchant's own words for the person, in the same answer. Tendero follows it
exactly where somebody else's schema forced it to (the UCP bindings) and
nowhere else:

- **54** money fields are `number:double`; **36** of the 49 schemas that carry
  one have no currency of their own; the wire has no money type at all, though
  the domain has had `Money` since the first slice.
- **40** declared 4xx bodies are a bare `string`, 25 more are empty.
- The shop's own **8** MCP tools declare no output schema.
- No client can name the version of the contract it was written against.

None of this is visible to a shopper today. It is the floor the agent surfaces
stand on, and two more are about to be built on it.

## The rule

**An answer has two readers and serves both from one place.** What a machine
needs is a value with its unit and a code from a closed set; what a person
needs is the shop's sentence, in their language. The sentence never carries a
fact the data does not, and the data never has to be parsed out of a sentence.

## Behaviour

### 1 · Money on the wire

`Money` is serialised, by one converter in the SharedKernel, as

```json
{ "amount": 11390, "currency": "EUR" }
```

- `amount` is an **integer in the currency's minor unit** (ISO 4217): the
  author's first decision, and what UCP, ACP and Stripe do. `currency` is the
  three-letter code, upper case.
- **Every view that carries money carries `Money`**, never a flattened number.
  A line, an option, a total, a figure on the dashboard: each is readable
  alone.
- **A request takes the same shape** wherever it takes an amount: the kit's
  budget, a price filter in a body. A query string keeps a plain number in
  major units (`?maxPrice=80`) — it is typed by a person into an address — and
  the spec of each such parameter says so.
- **The exponent stops being a constant in the converter.** `Money.Decimals`
  is two today and the catalogue is in euros; the converter asks a table by
  currency, which today has the currencies the seed uses. Multi-currency stays
  deferred; what ends is the assumption being written into arithmetic.
- **Rounding happens once, at the boundary the domain already has.** A value
  that is not a whole number of minor units when it reaches the wire is a bug
  in whoever produced it, and the converter throws rather than rounds.

The UCP mappers lose `Minor()` and `Major()`: both contracts now say money the
same way. The front ends' `price` pipe takes the object and formats it in the
reader's culture — the author's second point: the API returns the value and
the currency, the client formats.

### 2 · A refusal is a problem with a code

Every 4xx the API answers on purpose is `application/problem+json` (RFC 9457):

```json
{
  "type": "https://tendero.dev/problems/price-changed",
  "status": 409,
  "code": "PriceChanged",
  "title": "The price changed while you were here.",
  "detail": "It is now 113,90 €. Check it and pay again.",
  "path": "$.quoteHash"
}
```

- **`code`** is a member of a closed set **declared for the operation** (ADR
  0043): the contract says which refusals an endpoint has, and a client
  branches on the code, never on the words.
- **`title` and `detail` are the shop's sentences in the culture asked**
  (invariant 6), with `Content-Language`. They are what a host shows a person
  and what a model reads to correct itself.
- **`path`**, where the refusal is about one part of the request, is a JSONPath
  to it — ACP's `param`.
- **What a refusal carries besides** — the new total when the price changed,
  the warehouses that could not restock — stays as typed members beside these:
  the ten conflict records that exist become problems with extensions, and
  keep their fields.
- A 404 for *no such thing* gets a body like any other: `NotFound`, and what
  was not found.
- Validation failures keep ASP.NET's `errors` map and gain `code:
  "Invalid"`.

### 3 · A message is a code and a sentence

One type for what the shop says beside an answer:

```json
{ "code": "OverBudget", "about": "board", "text": "Sin tabla de cortar: lo más barato cuesta 24,90 € y quedaban 7,20 €." }
```

The kit's explanation is the first user (its `KitSentence` gains the code and
becomes this). A front end composes its own sentence from the code and the
answer's data, formatted for its reader; an agent with no front end reads
`text`. The two wordings are tied by the code: they cannot be about different
things.

### 4 · The shop's own MCP tools

Each returns `structuredContent` with a declared `outputSchema`, **and** the
same JSON as a text block (MCP's SHOULD, because there are clients that hand a
model only `content`). A refusal is a tool result with `isError`, carrying the
same problem as REST does — one refusal, two transports.

### 5 · A version a client can name

A dated version (`2026-10-13`) in the document's `info.version`, echoed in a
`Tendero-Version` response header. No negotiation: there is one version. The
day there are two, the header is where a client says which.

## Non-goals

- **Merging our contract with UCP's.** Two schemas is right: UCP's is somebody
  else's. This brings ours to the same conventions.
- **Multi-currency.** The exponent table is the seam; a second currency is not
  built.
- **Cursor paging, request signing, webhooks** — what UCP and ACP have that a
  shop's own front ends do not need. Written in the analysis, not taken up.
- **A model rewording anything.** Open in the kit solver's spec; on the agent
  surfaces the host's own model is the one that rewords.
- **Compatibility with the old shapes.** There is no external client of our
  own contract: the front ends, the simulator and the tools change in the same
  phase. The public repository gets it whole, at the article's tag.

## Measures

- **A contract test that fails on a money field that is a number.** The
  committed document is walked: no property whose schema is money may be
  `number`, and every `Money` has both members. Seen red on today's document.
- **A contract test that every declared 4xx is `problem+json` with a `code`
  enum**, and none is a `string`. Seen red on today's forty.
- **The converter, by properties**: write-then-read is the identity for every
  whole number of minor units; a value that is not whole throws; the exponent
  is the currency's.
- **UCP's contract suite unchanged and green**: its money was already this.
- **Each MCP tool's `structuredContent` validates against its `outputSchema`**,
  and its text block parses to the same value.
- **Both front ends' specs, and the two browser suites**, on the new shapes.
- **The search gate and the two golden sets** run: nothing here touches
  ranking or a model's prompt, and they say so by not moving. Tino's read
  tools return facts as sentences, which keep their wording.
- **Stryker at the close**, over the SharedKernel and what the sweep touched.

## Evidence of use

No event of its own: this is the contract, not a feature. What it makes
possible to see is already counted — `tendero.agent.tool_calls` by outcome,
where a *refused* now has a code a dashboard can group by. That tag is added:
`refusal`, the problem's code. **Question**: what do agents get refused for?
**Decision it moves**: a code that dominates is a tool whose description or
contract is wrong for an agent.

## Decisions taken with the author (2026-10-13)

1. **Money is an integer in minor units with its currency**, on our own wire
   as on UCP's.
2. **The whole of the audit**: money, errors, messages, the MCP tools' output
   schemas, a version.
3. **Before the kit's page**: phase 13c stops after K-4 and resumes at K-5 on
   the new shapes.
4. **The front end composes and formats**; the API returns the value, the
   currency, the code — and its own sentence for a reader with no front end.

## Open, for the author's reading of this text

- **A query-string amount stays in major units** (`?maxPrice=80`). The
  alternative is minor units there too — consistent, and `?maxPrice=8000` in an
  address a person reads.
- **`type` URIs under `https://tendero.dev/problems/…`**: a URI RFC 9457 asks
  for and nobody has to serve. The alternative is `about:blank` and the `code`
  alone.
- **The branch**: `feature/phase-13b-wire`, cut from the kit solver's branch
  at K-4, with K-5 resuming on top of it — one stack, in the order built.

## Building it, in order

1. **W-1 · Money.** The converter and its properties; the two contract tests,
   red; then the sweep, context by context — Pricing, Ordering, Search,
   Catalog, Inventory, the dashboard — each with its tests, the OpenAPI
   document and the generated types regenerated once at the end of each. The
   UCP mappers. The kit's API (budget in, `Money` out).
2. **W-2 · The front ends on `Money`.** The `price` pipe, the stores, the two
   apps; the browser suites.
3. **W-3 · The simulator and the tools** that read an amount.
4. **W-4 · Problems.** The shape, the per-operation codes, the forty strings
   and the twenty-five empty bodies; the sentences in es and en; the front
   ends branching on codes.
5. **W-5 · Messages.** The type; the kit's explanation on it.
6. **W-6 · The MCP tools**: structured content, output schemas, the text
   block, refusals as results.
7. **W-7 · The version.**
8. **W-8 · Close**: the ADR (*the wire has a money type; a refusal is a
   problem with a code; a message is a code and a sentence*), `CLAUDE.md`'s
   conventions, `docs/mcp.md`, Stryker, the notebook and the book's map.
