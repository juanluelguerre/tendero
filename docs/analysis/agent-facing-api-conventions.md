# An API with two readers: what MCP, UCP, ACP and WebMCP converge on, and where Tendero stands

Written 2026-10-13, from a question the kit solver raised (phase 13c): the
kit's explanation was sentences with amounts written into them, the author said
*the API returns the value and the currency and the front end formats* — and the
next sentence was *but an agent over MCP has no front end*. Both are right, and
the protocols written for agents in 2025–2026 have an answer to it that is
consistent enough to call a convention.

**Status**: analysis. Nothing here is decided; the decisions it asks for are at
the end. Sources are listed last, with what was and was not verified.

## What the specifications say

| | Money | Text for a person | Data for the machine |
|---|---|---|---|
| **MCP** (read at 2025-11-25; current is 2026-07-28 — see *Second pass*) | not its business | `content`: what the model reads | `structuredContent` (JSON) and an optional `outputSchema`; with one, the server MUST conform and the client SHOULD validate |
| **UCP** | integers in the currency's minor unit, ISO 4217 | `messages[].content` | `type`, `code`, `severity`; one set of schemas under REST, MCP, A2A and the embedded binding |
| **ACP** (OpenAI, Stripe) | integers in minor units; ISO 4217 in lower case | `totals[].display_text` — *the text displayed to the customer for this total* — and `messages[].content`, with `content_type` plain or markdown | a `code` from a closed set and `param`, a JSONPath to what the message is about |
| **WebMCP** (W3C draft) | not its business | examples return MCP's `content` | an output schema is listed as an open question |

Three details that matter more than the table:

- **MCP says to send it twice.** *For backwards compatibility, a tool that
  returns structured content SHOULD also return the serialized JSON in a
  TextContent block.* It is not a formality: there are clients that hand only
  `content` to the model and drop the structured half.
- **A business refusal is a result, not a fault.** MCP's tool execution errors
  travel inside the result (`isError: true`) with *actionable feedback that
  language models can use to self-correct*; protocol errors are for a request
  that was not one. UCP and ACP do the same with `messages`.
- **UCP's transports are thin by rule.** *Transport definitions MUST be thin:
  they declare method names and reference base schemas only.*

## The convention

1. **Money is a value and a currency, and the value is not a float.** The two
   commerce protocols written for agents both use integers in minor units with
   an ISO 4217 code. Outside them there is a second accepted form — a decimal
   written as a string (Google's `Money` splits it into `units` and `nanos`;
   Shopify's storefront API is remembered as a decimal string with a
   `currencyCode`, not re-verified here). What nobody specifies is a JSON
   number with a fraction, which a JavaScript client reads as a binary double.
2. **A closed set travels as a code**, with a path to what it is about.
3. **The merchant also writes the sentence a person reads**, labelled as
   display text, beside the data and never instead of it. This is the new part.
   The client is no longer the merchant's front end: it is somebody else's host,
   which shows the merchant's words or hands them to its model.
4. **One schema, several transports.**
5. **The output has a declared schema.**

So *the API returns data and the client formats* still holds for a client that
is ours. For one that is not, the API returns data **and** its own words.

## Where Tendero stands

Measured on the committed OpenAPI document (103 operations, 296 schemas) and
the MCP project, 2026-10-13.

### Already there

- UCP's bindings: money in minor units, `messages` for a search that finds
  nothing, `structuredContent` with an `outputSchema` on the checkout tools.
- A closed set is an enum by name on the wire (ADR 0043): 57 declared.
- Language is negotiated and declared (`?culture=`, `Content-Language`,
  ADR 0013), and the shop's own MCP tools label the culture they answered in.
- The shop's own MCP tools dispatch the same queries the REST endpoints do and
  return the same records: for *our* contract, one schema under two transports
  already.
- User-facing text is the shop's, in es and en (invariant 6); Tino's facts and
  now the kit's explanation are sentences the shop wrote.

### Not there

| Finding | Measure | Against |
|---|---|---|
| **Money is a JSON number with a fraction** | 54 fields typed `number:double` | convention 1 |
| **A money value without its currency beside it** | 36 of the 49 schemas that carry one | convention 1 — `OrderLineView`, `ShippingOptionView`, `OfferView`, `SalesFigures`… each is unreadable alone |
| **No `Money` type on the wire at all** | the schema does not exist; every view flattens to a number, and the currency is somewhere above it | one schema |
| **An error is an English sentence** | 40 declared 4xx bodies are a bare `string`; 25 more have no body; 10 are typed conflicts; 1 is `problem+json` | conventions 2 and 3 — no code a client can branch on, no path, no language |
| **The shop's own MCP tools declare no output schema** | 8 tools, none with `UseStructuredContent` | conventions 4 and 5, and MCP's own SHOULD |
| **No version a client can ask for** | `info.version` is `1.0.0`; no header, no negotiation | UCP's dated versions, ACP's `API-Version` |
| **The exponent is a constant** | `Money.Decimals = 2`; UCP's mapper multiplies by a hundred | ISO 4217 — a laboratory simplification, written down as one |

The first three are one finding seen three ways: **the domain has `Money`, the
wire does not.** Every slice flattens it by hand, each its own way, and a
schema with `total: 113.9` and no currency is the result.

### Two schemas, and that is right

Tendero has two contracts and will keep two: **its own** (REST and the shop's
MCP tools) and **UCP's** (REST, MCP today, A2A when it comes — UCP defines that
binding, so A2A adds a transport and no schema). UCP's shapes are somebody
else's and are reached through mappers. The question this analysis raises is
not whether to merge them. It is how far our own contract should move towards
the conventions UCP's already follows — at which point the mappers get thinner
and a client that knows one finds nothing surprising in the other.

## What it would change

**The architecture does not change. The wire does.** No context moves, no port
is added. What changes is in the SharedKernel and in how a slice shapes its
answer:

1. **A wire type for money**, once: `Money` serialised by one converter, as
   `{ "amount": 11390, "currency": "EUR" }`, used by every view instead of a
   flattened decimal. The UCP mappers lose `Minor()` and `Major()`. Both front
   ends' `price` pipe takes the object.
2. **One error shape**: RFC 9457 `problem+json` carrying a `code` from a closed
   set per operation, a `path` where there is one, and the sentence — in the
   culture asked. The 40 bare strings become codes with text.
3. **One message shape** for what is said beside an answer — the kit's
   explanation, a refusal, a notice — a `code`, what it is about, and the
   shop's sentence.
4. **The shop's MCP tools** return `structuredContent` with an `outputSchema`
   and the same JSON as text.
5. **A version** a client can name. Smallest honest form: a dated version in
   the document and a header that echoes it; no negotiation until a second
   version exists.

**Size.** The money type is the large one: 54 fields in 49 schemas, both front
ends, every test that reads an amount, the traffic simulator, the evaluation
tools. The errors are forty call sites and their tests. The other three are
small. None of it is architectural risk; all of it is breadth.

**What it does not buy.** Nothing a shopper sees. It is a contract brought to
where its neighbours are, done before more surfaces are built on it — the kit's
page (K-5) and `build_kit` over MCP (K-7) are the next two.

## Decisions for the author

1. **Money on our own wire.** (a) Integer minor units and a currency, as UCP,
   ACP and Stripe — the mappers become identity and one rule covers every
   surface; or (b) a decimal string and a currency — nearer what a person
   reads, and a second convention beside UCP's in the same server. The
   analysis recommends (a).
2. **Scope.** The whole of *Not there*, as a phase of its own before K-5; or
   the money type and the messages now, and the errors as their own phase.
3. **Where the phase goes**: before the kit's page, so the page is built once;
   or after phase 13c, with the page rebuilt on the new shapes.
4. **An ADR**: *the wire has a money type, a message is a code and a sentence,
   an error is a problem with a code* — written when the first of them is built.

## Second pass, the same day: against the primary sources

The author asked, before the sweep, that the change be held to the
specifications themselves and that every surface be audited. Two things in the
text above were wrong, and four surfaces turned out to have gaps this analysis
had not looked for. What was read this time: UCP's schemas at the tag
`v2026-08-25` and live at `ucp.dev`; ACP's repository at `spec/2026-04-17`;
MCP's revision **2026-07-28**; the WebMCP draft of 9 October 2026; our code.

### What was wrong above

- **MCP's current revision is 2026-07-28, not 2025-11-25.** The rule quoted —
  structured content, and the same JSON as text — stands (2026-07-28 loosens
  `structuredContent` to any JSON value). What the newer revision adds was not
  counted: no sessions and no handshake, `server/discover`, caching hints on
  `tools/list`, required request headers.
- **"An amount without its currency beside it" is not against the convention.**
  UCP does both, by design: in the catalogue a price is `{amount, currency}`
  (`price.json`: *Price with explicit currency*); in a checkout the currency is
  said once at the top and every amount is a bare integer (`total.json`,
  `item.json`). ACP is the same — an object in its feed, bare integers in its
  checkout. What both forbid is the fraction: *UCP does not put floating-point
  numbers on the wire.*

### The money shape, held to the schemas

`{"amount": 11390, "currency": "EUR"}` is **UCP's `price.json` and ACP's feed
`Price`, member for member**: `amount` an integer in ISO 4217 minor units,
`currency` matching `^[A-Z]{3}$`. Read live, 2026-10-13.

| Representation | Who | Amount | Currency |
|---|---|---|---|
| `{amount, currency}` | UCP `price.json`; ACP feed `Price` | integer, minor units, ≥ 0 | beside each value, upper case |
| a bare integer | UCP checkout; ACP checkout; Stripe | integer, minor units (signed in UCP's totals) | once per resource |
| `{amount, currency}`, decimal | Zalando's guidelines, Shopify | a decimal in the major unit | beside each value |
| `{currency_code, value}` | PayPal | a decimal in a string | beside each value |
| `{currency_code, units, nanos}` | Google (AIP-143) | int64 and int32 | beside each value |

Three things the schemas say that our converter did not:

- **UCP bounds the integer at 2⁵³ − 1**, the largest a JavaScript client reads
  exactly. Ours declares `int64`. To be declared.
- **UCP's `amount` is not negative**; a discount in a total is a separate
  `signed_amount`, a bare integer. Ours writes a negative `Money` as the same
  object. Legitimate in our own contract and not a UCP type: to be decided once
  — a discount is a positive amount under a name that says discount, or it is
  signed.
- **The client needs the exponent table**, and a price below a minor unit does
  not fit (UCP has a separate `unit_price` for it). Known costs of the form.

**One rule on our wire** — the object everywhere — is a choice, not the only
conformant one: UCP itself says the currency once in a checkout. The object
costs a few bytes and lets a mixed-currency answer be written, which a
validator has to refuse; it buys a value that is readable wherever it is, and
one type for the front ends. The UCP mappers do **not** become identity, as
the spec first said: they still write bare integers under a top-level currency
where UCP's checkout wants them.

### Every surface, audited

Severity is the specification's own word. *Verified* means read in our code or
against the live source in this pass; the rest is one audit's reading.

**Our REST API**

| Finding | Measure | Severity |
|---|---|---|
| A refusal is a bare sentence | 40 of the declared 4xx bodies | convention (RFC 9457) |
| The document declares no security | no scheme; one operation declares a 401 | convention — a reader cannot tell what needs a token |
| Creating answers 200, never 201 with `Location` | 40 POSTs, 40 × 200 | SHOULD (RFC 9110) |
| A rate-limited operation declares no 429 | none | convention |
| The idempotency key is a body member, on one operation | `PlaceOrder` | convention (ACP and the IETF draft use a header) |

**The MCP server** (SDK 2.2.0, which speaks 2026-07-28)

| Finding | Severity | Verified |
|---|---|---|
| `Origin` is not validated on `/mcp` | MUST | not found in the repo or the SDK's handler; **not yet tried with a request** |
| No rate limit on `/mcp` | MUST | verified: the three policies are Steward's, Tino's and visual search's |
| No tool carries annotations, so eleven read-only tools are announced with the defaults — not read-only, destructive | SHOULD | verified |
| The shop's own eight tools declare no output schema | SHOULD | verified |
| A checkout call without a token is a tool result over HTTP 200: no 401, no `WWW-Authenticate`, no RFC 9728 metadata | SHOULD; MUST once MCP authorization is declared | verified in the code |
| `server/discover`, caching hints | MUST | not verified: the SDK has the types, nothing here configures or tests them |

*The last two, since (2026-10-13, against a running server).* `server/discover`
answers with the versions spoken and a version not spoken is refused with
`-32022`: the SDK does both, and a test now holds it. The hints were there
too, at the SDK's safe defaults (`private`, stale at once); the tool list now
says `public` and five minutes. And a checkout call with no token is a 401
whose challenge names RFC 9728 metadata — ADR 0059 says how.

**WebMCP**

| Finding | Severity | Verified |
|---|---|---|
| We call `navigator.modelContext.provideContext`; the draft of 9 October defines `document.modelContext.registerTool`, one call a tool | nothing registers in a conformant browser | our half verified; the draft's as the audit read it |
| The browser spec installs a fake of the old API, and passes | the test cannot see the above | verified |
| No annotations (`readOnlyHint`; `add_to_cart` changes state) | optional | verified |

**UCP** (we declare `2026-08-25`, which is the latest release; our 116 vendored schemas are identical to the tag's)

| Finding | Severity | Verified |
|---|---|---|
| Schema URLs are `https://ucp.dev/schemas/…`, which answer 404; the published ones are under `/2026-08-25/` | MUST (*a capability MUST declare a schema*, and the platform MUST fetch it) | verified with a request |
| The platform's protocol version is never validated; `version_unsupported` does not exist in the code | MUST | verified |
| No `Cache-Control: public, max-age ≥ 60` on the profile or the schemas | MUST | verified |
| Idempotency over REST: 409 for a key reused with another body, 24 h retention | MUST when a key is sent | read only in `complete`; the comparison was not found |
| Authority binding: a capability's schema must be served from the host its name reverses to | MUST | as the audit read it |
| `Minor()` multiplies by a hundred | — | verified: the exponent is a constant there too |

### What it means

**The money change is right, and it is the smallest part of what the audit
found.** The surfaces written for agents — the ones whose claim is conformance
— have MUSTs unmet that no test of ours could see, because our tests hold us
to our own reading: a vendored schema, a faked browser API, a handshake of an
older revision.

## For the blog and the book

- *An API with two readers* — data for the machine and the merchant's own words
  for the person, in one answer; why the merchant writes the words when the
  client is not its own.
- *Money is not a decimal* — minor units, ISO 4217, the exponent that is not
  always two, and finding fifty-four doubles in a contract whose domain had a
  `Money` type the whole time.
- *One schema, four transports* — REST, MCP, WebMCP and UCP over the same
  queries, with the kit as the worked example.
- *An error a model can act on* — forty English sentences against a code, a
  path and a sentence; what a model does with each.
- *Conformant to our own reading* — a vendored schema, a faked browser API
  and a handshake of an older revision: three tests that were green and held
  us to nothing but ourselves. What an audit against the live source found.

## Sources, and what was verified

- MCP, tools, 2025-11-25 — read whole:
  <https://modelcontextprotocol.io/specification/2025-11-25/server/tools>
- ACP, agentic checkout — read through a summary of the page; its examples do
  not all match its schema (one amount is a string):
  <https://developers.openai.com/commerce/specs/checkout>
- UCP, overview — **the first half only**; the values of `severity` and the
  detail of `messages` were not verified:
  <https://ucp.dev/latest/specification/overview/>, and its reference
  <http://ucp.dev/2026-04-08/specification/reference/>
- WebMCP — a draft, and secondary sources disagree on the interface's name:
  <https://webmachinelearning.github.io/webmcp/>
- Google's `Money` (`units` and `nanos`) and Stripe's minor units — from a
  search's summary of their references. **Shopify's `MoneyV2` is from memory.**
- The clients that drop `structuredContent`: reported in
  <https://github.com/andreban/webmcp-skills/issues/85> and a GitLab issue of
  `rmcp-openapi`; not reproduced here.

## References, each one opened on 2026-10-13

The list above is the first pass's, kept as it was written. This one is every
standard the analysis, the wire spec and ADR 0059 lean on, with what the page
was seen to say that day. *Opened* means the address answered and said what is
claimed here; where only part of a long page was read, that is said.

**Agent protocols**

| Standard | What the page said | Address |
|---|---|---|
| UCP, the Universal Commerce Protocol | the home names no version; its links go to `/latest/` | <https://ucp.dev> |
| UCP, overview at `2026-08-25` | examples use version `2026-08-25`; `Cache-Control` public with `max-age` of at least 60 on published artefacts; a 409 for a key reused with another payload; `UCP-Agent` as an RFC 8941 dictionary. The first half of the page only | <https://ucp.dev/2026-08-25/specification/overview/> |
| UCP, `price.json` | *Price with explicit currency*: `amount` in ISO 4217 minor units, `currency` matching `^[A-Z]{3}$`, both required | <https://ucp.dev/2026-08-25/schemas/common/types/price.json> |
| MCP, revision 2026-07-28 | the specification's index, on the schema of `2026-07-28`; stateless requests, no handshake | <https://modelcontextprotocol.io/specification/2026-07-28> |
| MCP, tools | `structuredContent` any JSON value, and SHOULD also be serialised in a text block; `outputSchema`; annotations untrusted unless the server is; a tool's own error is a result with `isError`; servers MUST rate limit tool invocations | <https://modelcontextprotocol.io/specification/2026-07-28/server/tools> |
| MCP, Streamable HTTP | servers MUST validate `Origin`, and answer 403 to an invalid one; `MCP-Protocol-Version`, `Mcp-Method` and `Mcp-Name` required on a request | <https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http> |
| MCP, authorization | optional; when supported, RFC 9728 metadata is a MUST and a missing or invalid token a 401 with `WWW-Authenticate` | <https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization> |
| WebMCP | *Draft Community Group Report, 9 October 2026*, Web Machine Learning Community Group; `document.modelContext`, `registerTool`, `ToolAnnotations` with `readOnlyHint`, `untrustedContentHint`, `consequentialHint`, `debugging`. The first 100,000 characters of 139,000 | <https://webmachinelearning.github.io/webmcp/> |
| WebMCP, its repository | the draft's source and explainers | <https://github.com/webmachinelearning/webmcp> |
| ACP, the Agentic Commerce Protocol | the home; developed by Stripe and OpenAI, Apache 2.0; names no version | <https://agenticcommerce.dev> |
| ACP, its repository | `spec/2026-04-17` is the latest stable, beside an `unreleased` folder; it holds `json-schema`, `openapi` and `openrpc` | <https://github.com/agentic-commerce-protocol/agentic-commerce-protocol/tree/main/spec/2026-04-17> |
| AP2, the Agent Payments Protocol | v0.2; a checkout mandate and a payment mandate, each open and then closed; an extension to A2A and to UCP | <https://ap2-protocol.org/ap2/specification/> |
| A2A, the Agent2Agent Protocol | 1.0.0 the latest released; with the Linux Foundation. Not used by Tendero yet | <https://a2a-protocol.org/latest/specification/> |

**HTTP and REST**

| Standard | What it is cited for | Address |
|---|---|---|
| RFC 9457, *Problem Details for HTTP APIs* (2023, obsoletes RFC 7807) | a refusal's shape | <https://www.rfc-editor.org/rfc/rfc9457> |
| RFC 9110, *HTTP Semantics* (2022) | `201` and `Location`, §15.3.2; `Retry-After`, §10.2.3 | <https://www.rfc-editor.org/rfc/rfc9110> |
| RFC 6585, *Additional HTTP Status Codes* (2012) | `429 Too Many Requests`, §4 — **it is this RFC and not 9110 that defines 429** | <https://www.rfc-editor.org/rfc/rfc6585> |
| RFC 9535, *JSONPath* (2024) | a problem's `path` | <https://www.rfc-editor.org/rfc/rfc9535> |
| *The Idempotency-Key HTTP Header Field*, an IETF draft | the header's name and meaning. **An expired Internet-Draft** (`-07`, 15 October 2025): a convention, not a standard | <https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/> |
| Stripe, idempotent requests | the convention in practice: the `Idempotency-Key` header, the parameters compared with the first request's and an error when they differ, keys kept at least 24 hours | <https://docs.stripe.com/api/idempotent_requests> |
| OpenAPI 3.2.0 (19 September 2025) | the version our document declares; `latest` is 3.2.1, of 10 September 2026 | <https://spec.openapis.org/oas/v3.2.0.html> |

**Identity and authorization**

| Standard | What it is cited for | Address |
|---|---|---|
| RFC 9728, *OAuth 2.0 Protected Resource Metadata* (2025) | what MCP's authorization asks of a 401; not built here (ADR 0059, *Left open*) | <https://www.rfc-editor.org/rfc/rfc9728> |
| RFC 6750, *Bearer Token Usage* (2012) | the bearer scheme the contract declares, and `WWW-Authenticate` | <https://www.rfc-editor.org/rfc/rfc6750> |
| RFC 8693, *OAuth 2.0 Token Exchange* (2020) | the `act` claim, §4.1: an agent acting for a person | <https://www.rfc-editor.org/rfc/rfc8693> |
| RFC 8941, *Structured Field Values for HTTP* (2021) | the `UCP-Agent` header. **Obsoleted by RFC 9651** (2024); UCP's overview still cites 8941 | <https://www.rfc-editor.org/rfc/rfc8941>, <https://www.rfc-editor.org/rfc/rfc9651> |

**Money**

| Standard | What it is cited for | Address |
|---|---|---|
| ISO 4217, at its maintenance agency (SIX) | the currency codes and their minor units; *List One* is the current table. ISO's own page (<https://www.iso.org/iso-4217-currency-codes.html>) refused the request with a 403, so it was not read | <https://www.six-group.com/en/products-services/financial-information/data-standards.html> |
