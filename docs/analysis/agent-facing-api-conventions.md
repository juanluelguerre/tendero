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
| **MCP** (2025-11-25) | not its business | `content`: what the model reads | `structuredContent` (JSON) and an optional `outputSchema`; with one, the server MUST conform and the client SHOULD validate |
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
