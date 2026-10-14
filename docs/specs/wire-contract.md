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
  budget, a price filter in a body. A query string carries the bare whole
  number of minor units (`?maxPrice=8000`), the currency being the shop's —
  first written here as major units, and decided otherwise (see *Decided by
  the author*, below).
- **The exponent stops being a constant in the converter.** `Money.Decimals`
  is two today and the catalogue is in euros; the converter asks a table by
  currency, which today has the currencies the seed uses. Multi-currency stays
  deferred; what ends is the assumption being written into arithmetic.
- **Rounding happens once, at the boundary the domain already has.** A value
  that is not a whole number of minor units when it reaches the wire is a bug
  in whoever produced it, and the converter throws rather than rounds.

**It is UCP's `price.json` and ACP's feed `Price`, member for member** — read
against the live schemas on 2026-10-13 (the analysis, *Second pass*). Two
things follow from holding it to them. The integer is bounded at 2⁵³ − 1, as
UCP's is: the largest a JavaScript client reads exactly. And one rule on our
wire — the object everywhere — is a choice: UCP itself says the currency
once at the top of a checkout and writes bare integers under it. So the UCP
mappers get one exponent table instead of a `* 100`, and do not disappear.

The front ends' `price` pipe takes the object and formats it in the reader's
culture — the author's second point: the API returns the value and the
currency, the client formats.

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

## Found by the second pass, and taken into the phase

The audit of every surface (the analysis, *Second pass*) found more than this
spec set out to fix. The author took all of it into the phase on 2026-10-13
(*adelante con todo*), and it was built with the rest — see *Built*, below:

- **REST**: no security declared in the document; 200 where 201 with
  `Location` belongs; no 429 declared; the idempotency key in a body.
- **MCP**: `Origin` not validated and no rate limit on `/mcp` (both MUST);
  no annotations, so read-only tools are announced as destructive; a
  checkout call without a token answers 200.
- **WebMCP**: the storefront calls an API the current draft no longer
  defines, and its browser spec fakes the old one.
- **UCP**: schema URLs that answer 404, no validation of the platform's
  version, no `Cache-Control` on the profile (all MUST); idempotency's 409.
- **Money**: whether a discount is a negative `Money` or a positive one
  under its name.

## Built (2026-10-13), and what is still owed

Everything above is built on `feature/phase-13b-wire`, recorded in ADR 0059:

- **Money**: the sixty-two fields the audit found are `Money`; the ratchet in
  `WireMoneyContractTests` is empty. Both front ends format and never divide.
- **Refusals**: every 4xx the contract declares is `application/problem+json`
  with a closed set of codes (`WireConventionsContractTests`): the forty bare
  strings, the twenty-six empty bodies and the ten conflict records.
- **Messages**: `Message<TCode>`; the kit's explanation carries `KitSaid`.
- **REST**: the version and its header, the bearer scheme and each operation's
  policies, `201` with `Location` on the three operations that create something
  with an address, `429` with `Retry-After`, `Idempotency-Key` in its header.
- **MCP**: `Origin` validated, tool calls limited, every tool annotated, the
  shop's own tools with structured content and an output schema, and refusing
  with the same problem. UCP's protocol errors are JSON-RPC errors with its codes.
- **UCP**: versioned schema URLs, `Cache-Control`, `version_unsupported`,
  idempotency with its 409, the exponent table, and the authority binding of
  the shop's own extension (`Ucp:OwnSchemasUrl`).
- **WebMCP**: `document.modelContext.registerTool`, one call a tool, with
  `readOnlyHint`, as the draft of 9 October 2026 defines it.
- **After a walk through both front ends in a browser** (the same day): a
  checkout tool with no token is a 401 with `WWW-Authenticate` and RFC 9728
  metadata, a person's token a 403, a token that is not good a 401;
  `server/discover` and the tool list's caching hints are held by a test, the
  list `public` for five minutes; and both front ends ask the API in the
  language the screen is in (`tenderoCultureInterceptor`) — a Spanish
  backoffice in an English browser was being refused in English.
- **A promotion that did not apply says so as a message**: `reasonCode` a
  closed set, `requires` (the condition, money as `Money`), `shortBy` and
  `unitsShort` (how far the cart is, worked out by the shop), `winner`; the
  storefront composes its sentence and the server's stays for an agent.
- **The till asks the shelf before the bank** (ADR 0025, amended): a cart that
  asks for more than there is is a 409 `InsufficientStock` carrying each
  article's asked and available, with nothing charged; a basket line carries
  `available`; and a status whose refusals carry different things declares
  each by its code.

Still owed before the phase is closed: **Stryker** over what it touched, the
notebook and the book's map — and the author's reading of the three points
below.

## Decided by the author on 2026-10-13, after reading this text

- **A price is asked for in the unit it is answered in.** This text proposed
  major units in a query string (`?maxPrice=80`), for the person who types
  it. Decided the other way once it was seen who types it: the filter was also
  the MCP tool's, described as *Lowest price, inclusive* with no unit, beside
  facets whose ends are minor units — an agent that sent a facet's `3000`
  back was asking for what costs three thousand euros, and got an empty page
  with no error. `minPrice` and `maxPrice` are whole minor units on REST and
  on MCP; a fraction is refused by name. The storefront's own address keeps
  what a person types (`/c/HOME?price=30-60`) and converts when it asks. The
  shop's own assistant's tool keeps major units, in and out: its reader is a
  small model that says what it is handed.
- **`type` URIs under `https://tendero.dev/problems/…`**: accepted as built,
  behind its one constant (`ProblemTypes.Base`).
- **Where the shop's own UCP schema is published: nowhere, for now.** A
  platform accepts `com.elguerre.tendero.knowledge` only from a host under
  `elguerre.com` (UCP's authority binding), and nothing a local run can show
  depends on it: `Ucp:OwnSchemasUrl` stays empty, the shop serves the schema
  itself, and a platform that follows the binding to the letter drops that one
  extension and goes on with UCP's own. It is a deployment step — `docs/mcp.md`
  (*An agent of your own, on your own machine*) and the README say so.
- **An agent's registration and a return keep answering 200.** A 201 names
  where what was created lives, and neither has an address of its own; nothing
  reads one today, and a return's would be a public endpoint with the order's
  access rule to keep, added for a status code.
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
