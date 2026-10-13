# 0059 — The wire has two readers: money in minor units, a refusal is a problem with a code, a message is a code and a sentence

**Status**: accepted (phase 13b, 2026-10-13). The decisions are the wire spec's
(`docs/specs/wire-contract.md`), taken with the author that day after the audit
in `docs/analysis/agent-facing-api-conventions.md`. What implementing them added
is marked *found while building*.

## Context

The shop's own API was written for its own two front ends, and read by them
alone. It said money as a JSON number with a fraction and a `currency` somewhere
else in the answer; it said *no* as a sentence in English in a bare string, or
as a status with no body; and what it explained beside an answer — why a kit is
what it is — it explained in prose a client could only show.

Each of those is harmless to a front end written in the same repository, which
knows where the currency is and never branches on a refusal. None is harmless to
the second reader the project is about: an agent, with no front end and no
knowledge of the routes. MCP, UCP, ACP and the WebMCP draft were read side by
side (the analysis), and they agree with each other and disagreed with us on
exactly these three points. UCP's own half of the shop already did all three,
because its specification makes it — so the shop was speaking two dialects, and
the careful one was the one somebody else had written.

## Decision

**Money is a whole number of the currency's minor units, with its currency.**
`{"amount": 11390, "currency": "EUR"}`, everywhere an amount is on our wire: one
converter (`WireJson.Money`), registered for the API, the MCP server and the
read tools, so a slice hands over a `Money` and never spells it. Member for
member UCP's `price.json` and ACP's feed `Price`. The exponent is the
currency's, from one table (`MinorUnits`) that UCP's mappers read too; the
amount is bounded at 2⁵³−1 either way, the last whole number a JavaScript reader
holds exactly. The converter refuses a fraction of a minor unit rather than
rounding it: rounding happens once, where the domain rounds. A discount is an
amount, never negative — its name says which way it goes.

**A refusal is an RFC 9457 problem with a code.** `application/problem+json`,
with a `code` from a closed set **declared for the operation and the status**,
the shop's `title` and `detail` in the language asked, the part of the request
it is about as a JSONPath (`path`), and what it carries — the new totals when a
price moved — as members beside the problem's own. A client branches on the
code; the sentences are for whoever reads them and may be reworded.

**A message is a code, what it is about, and a sentence.** `Message<TCode>`,
first used by the kit's explanation. A front end composes its own sentence from
the code and the answer's fields; an agent with no front end reads the text. The
code ties the two wordings.

**The contract says what it is.** A dated version in `info.version`, repeated by
every answer in `Tendero-Version`; the bearer scheme and, per operation, the
policies it asks for, taken from the endpoint's own metadata; `201` with
`Location` for what creates something with an address; `429` with `Retry-After`;
the idempotency key in the `Idempotency-Key` header.

**One refusal, two transports.** The shop's own MCP tools refuse with the same
problem, as the text of a tool result marked as an error.

## How a refusal is built, and why the kernel stays free of HTTP

A refusal is **data in the SharedKernel** — `Refusal(code, status, title)` — and
nothing of HTTP. A slice answers one with the framework's own result and names
it in its endpoint's metadata:

```csharp
QuoteOutcome.UnknownSkus => TypedResults.Problem(
    statusCode: QuoteRefusals.UnknownSkus.Status,
    extensions: QuoteRefusals.UnknownSkus.Said(es: …, en: …, path: "$.lines")),
…
.WithMetadata(new Refuses(QuoteRefusals.UnknownSkus, QuoteRefusals.MixedCurrencies))
```

The host does the rest, once, for every slice (`Api/Problems.cs`): it finds the
refusal the code names, says the title and the detail in the language asked,
gives the problem its type, writes what it carries as members, and sets
`Content-Language`. The same place turns a status with no body — nobody signed
in, somebody who may not, nothing at that address, a limit passed — into the
refusal that status is. And one OpenAPI transformer declares all of it from the
same metadata, so the document cannot disagree with the endpoint.

The alternative considered and not taken was a result type of the project's own
(`Refused : IResult`), which every context would have to see — and the only
project every context sees is the SharedKernel, which would then reference
ASP.NET. The framework's `ProblemHttpResult` passes through
`IProblemDetailsService`, which is the seam that makes that unnecessary.

## Found while building

- **A price is asked for in the unit it is answered in.** The price facet's
  ends are money, and the filter beside them — `minPrice`, `maxPrice`, on REST
  and on the MCP tool — is a whole number of the same minor units, so a
  facet's end goes straight back. It was first left in major units, *typed by
  a person into an address*; but the same filter was an agent's, with no unit
  in its description, and a facet's `3000` sent back asked for what costs
  three thousand euros and was answered an empty page. A fraction is refused
  by name. The storefront's own address still reads as a person types it
  (`price=30-60`) and converts on the way out; the browser's `Intl` knows the
  exponent, so the front ends carry no table. The shop's own assistant's tool
  stays in major units both ways: its reader is a small model that says what
  it is handed.
- **An offer's headline figure was one number that meant three things**
  according to its kind — a percentage, an amount of money, a count. Each is now
  under its own name (`percent`, `amountOff`, `buy`, `free`).
- **A line's total is the server's.** The order page multiplied a unit price by
  a quantity; with money as an object that arithmetic had to be somebody's, and
  it is the order's (`OrderLineView.total`).
- **A chart's number is not the API's.** A chart computes ticks from the values
  it is given and hands back bare numbers; those are formatted by
  `formatMajor`, and nothing the API sent goes through it.
- **A domain invariant's sentence is English, written for a log.** Where a
  refusal is an aggregate's own no, its sentence travels as `reason` — shown on
  a shopkeeper's screen beside the shop's sentence, never to a shopper — and
  the title is the one in two languages. Making invariants carry codes of their
  own is not done here.
- **`Idempotency-Key` means one request.** The store that keeps a key with its
  request's fingerprint (`IIdempotencyStore`) serves UCP's checkout and the
  shop's own: the same key with another body is a 409, where it used to be
  answered the first order.
- **A promotion that did not apply was the first message the shop had, and it
  was half one** (found in a browser, the day the phase was built). It carried
  a code and a sentence — and the sentence was all a page could show: *un
  mínimo de 40.00 EUR y artículos de KITCHEN*. The code is a closed set now
  (`RuleReasonCode`), and the reason carries what the sentence is made of: the
  condition asked (`requires`), how far the cart is from it (`shortBy`,
  `unitsShort` — worked out by the shop, because a page adds no money) and the
  promotion that won instead (`winner`). The page composes its own; the
  server's sentence stays what an agent reads.
- **What a refusal carries goes with its code, not with its status.** A
  checkout's 409 became several refusals carrying different things, and the
  document said *a problem and all of them*. A status whose refusals differ in
  what they carry declares each as a branch (`anyOf`: its codes, and what
  those carry); one whose refusals agree is said as before.

## Left open, on purpose

- **The problem types' base URI** is `https://tendero.dev/problems/…`, one
  constant (`ProblemTypes.Base`). Nothing serves it and RFC 9457 does not ask
  that anything does; it should be a domain the project holds.
- **An agent's registration and a return** are created and answer 200: neither
  has an address of its own for a `Location` to carry. Kept so by the author
  (2026-10-13): nothing reads either by itself, and a return's address would
  be a public endpoint added for a status code.
- **The shop's own UCP schema is published nowhere**, and `Ucp:OwnSchemasUrl`
  stays empty: it is what a deployment for agents the shop does not control
  sets, and nothing a local run shows depends on it (`docs/mcp.md`).
- **UCP's tools keep UCP's own wording** for a refusal; only the shop's own
  tools carry the shop's problem.
- ~~**A checkout tool called with no token** answers a JSON-RPC error over HTTP
  200, not a 401 with `WWW-Authenticate`.~~ **Closed the same day**: `/mcp`
  stays anonymous for its reads (ADR 0039) and the door is decided per call —
  an endpoint filter reads which tool the call names and answers nobody with a
  401 whose challenge names RFC 9728 metadata, a person with a 403, and a token
  that is not good with a 401 on any call (`Mcp/McpAuthorization.cs`). The
  objection was reading the body before authorisation; it is one small JSON
  document, buffered and left where it was, and the alternative — the
  `Mcp-Name` header of the 2026-07-28 revision — would have left every earlier
  client behind the old answer.

## Consequences

- No field of money in the contract is a number with a fraction, and a test
  fails the day one is written (`WireMoneyContractTests`); every declared 4xx is
  a problem with a closed set of codes, and a test fails on one that is not
  (`WireConventionsContractTests`).
- The front ends format and never divide by a hundred; a refusal is read by one
  function in each (`refusalSentence`, `refusalOf`).
- UCP's mappers stop assuming two decimals. Multi-currency stays deferred; what
  ended is the assumption being written into arithmetic.
- There is no compatibility with the old shapes. The front ends, the browser
  suites and the tools moved in the same phase; the public repository receives
  it whole, at an article's tag.
