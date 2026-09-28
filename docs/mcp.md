# Connecting an agent to Tendero

Tendero exposes an **MCP server at `/mcp`** and a **UCP business profile at
`/.well-known/ucp`**. Both are read-only, both are anonymous, and both run inside
the API process.

This page is how you point a client at them. What they are and why they are
shaped this way is in [`architecture.md`](architecture.md#agent-surfaces); the
decisions are ADR 0020 and ADR 0021.

## Before you start

```bash
dotnet run --project src/AppHost
```

The API listens on **`http://localhost:5130`** with the default launch profile.
Confirm it in the Aspire dashboard if you changed anything — the port is the one
thing on this page that varies per machine.

```bash
curl http://localhost:5130/.well-known/ucp
```

If that returns JSON, everything below will work.

## Claude Code

```bash
claude mcp add --transport http tendero http://localhost:5130/mcp
```

And to be answered in English rather than Spanish:

```bash
claude mcp add --transport http tendero http://localhost:5130/mcp \
  -H "Accept-Language: en"
```

That header is not decoration. It is the middle link of ADR 0013's chain, and it
works here for the same reason it works in a browser: the streamable HTTP
transport is HTTP, so the header the client sets reaches the tools. Every tool
also takes an explicit `culture`, which wins over the header — an agent that
wants one answer in English asks for it per call.

## VS Code

`.vscode/mcp.json` in the workspace:

```json
{
  "servers": {
    "tendero": {
      "type": "http",
      "url": "http://localhost:5130/mcp"
    }
  }
}
```

## Claude Desktop

Remote servers are added through the interface rather than a config file:
**Settings → Connectors → Add → Add custom connector**, then paste
`http://localhost:5130/mcp`.

**Honest caveat.** Custom connectors are built for internet-hosted servers over
`https`, and a plain-`http` `localhost` URL may be refused depending on the
version. If it is, the usual workaround is a stdio-to-HTTP bridge in
`claude_desktop_config.json`, in the same `mcpServers` shape the documentation
uses for local servers. Neither path has been exercised on the machine this was
written on — what *has* been is `curl` against Kestrel, which is why the
`curl` above is the check that matters.

## Two protocols, one endpoint — and why that is correct

This confuses people, reasonably, so it is worth being explicit.

**MCP and UCP are not alternatives.** They sit at different layers. MCP is how a
model is handed tools: JSON-RPC, `initialize`, `tools/list`, `tools/call`. UCP is
a commerce protocol — it defines what a catalogue search *is*, what a Product
looks like, what money is in — and then names the transports it can be carried
over: REST, **MCP**, A2A, embedded.

So "UCP over MCP" is not a compromise. It is a binding the specification defines,
and it is the one this shop implements. What that means in practice is two
different ways in, and they discover the shop differently:

| | A plain MCP client | A UCP agent |
|---|---|---|
| **Starts at** | the endpoint you configured | `GET /.well-known/ucp` |
| **Then** | `initialize` → `tools/list` | reads `services["dev.ucp.shopping"]`, finds `transport: mcp` and its `endpoint` |
| **Calls** | any of the eight tools | the three the binding fixes |
| **Gets back** | this shop's own shapes, in `result.content` | the UCP envelope, in `result.structuredContent` |
| **Needs** | nothing | nothing yet — the read half is anonymous |

The same `/mcp` route serves both, because the binding says the transport is MCP
and there is one MCP server per process. A test follows the profile's own
`endpoint` and checks the binding's three tools are actually there, which is the
one seam neither protocol's tests can see: each half can be perfectly correct
about itself while the manifest points at nothing.

**Both of UCP's main bindings are served.** The profile declares two entries for
`dev.ucp.shopping` — `mcp` at `/mcp` and `rest` at `/ucp` — and an agent picks.
Over REST the three operations are the specification's own paths:

```
POST /ucp/catalog/search     body: catalog_search.json#/$defs/search_request
POST /ucp/catalog/lookup     body: catalog_lookup.json#/$defs/lookup_request
POST /ucp/catalog/product    body: catalog_lookup.json#/$defs/get_product_request
```

One thing about them goes against the reflex, and it is the specification's
choice rather than ours: **a lookup that finds nothing answers 200**, with a
`messages` entry naming the identifier. An unknown id is a business outcome, and
a 404 would tell a caller their request was malformed when it was not.
`POST /ucp/catalog/product` is the exception and does answer 404 — its response
schema requires a `product`, so there is no valid 200 shape for a miss.

**None of the UCP routes appear in `docs/openapi/tendero.json`**, and that is a
rule rather than an oversight (ADR 0030): a route whose own protocol publishes a
machine-readable description is described there, not a second time by us. The
same applies to `/mcp` and to the development issuer. It does not make them
private — they are public and callable either way.

## What the server offers

Eight tools, and they are two different contracts on one server.

### UCP's catalogue binding

The shapes the specification fixes, for an agent that speaks UCP. Money is in
**minor units** (39,00 € is `3900`); a search that finds nothing is a `messages`
entry, not an error; the payload arrives in `result.structuredContent`.

| Tool | What it does |
|---|---|
| `search_catalog` | Free-text discovery, with `filters.category` and cursor `pagination` |
| `lookup_catalog` | Resolve up to 50 identifiers at once |
| `get_product` | One product, with every option and every variant |

**`meta` is optional**, and when it is sent, `ucp-agent` is a nested object:

```json
{"meta": {"ucp-agent": {"profile": "https://your-platform.example/profile"}}}
```

Absent is fine. Malformed — a `ucp-agent` with no `profile` — is refused, and
says which member is missing.

**The shop's own extension, `com.elguerre.tendero.knowledge`** (P11-8). When the
profile is on a trusted host and speaks it, every product `lookup_catalog` and
`get_product` return carries `knowledge`. It holds the product's specs, each
with its `source`: `catalogue`, or `description` when a person approved it from
the product's own text. In that case it also carries the quoted words and how
well they support the value, 0 to 1. The member lists only the specs the shop
knows: a missing one means the shop does not know it. The answer's `ucp.capabilities`
names the extension. Its schema is the shop's own, served at
`/ucp/schemas/com.elguerre.tendero.knowledge.json` and linked from the profile.
Without a profile, or with one from an untrusted host, the catalogue answers as
before and nothing is refused. The development profiles speak it.

**A search says what kind of answer it is**, as informational `messages` (ADR
0032), over both the MCP and the REST binding:

| `code` | When |
|---|---|
| `no_exact_match` | no product carries every word of the query; the products are the closest things, not matches |
| `help_available` | the query asks about something with a page of its own — today returns, pointing at `/api/returns/policy` |

There were three. `not_sold` — "the shop does not sell that at all" — belonged to
a scope classifier that was measured and refuted, so it was never sent; the
classifier is evaluation tooling now and the message is gone (ADR 0032, amended).

Each is `type: info` with `path: $.query` and a sentence in the answered language,
and the products still come back beside it.

Every one of these answers is validated in the test suite against the
specification's own JSON Schemas, vendored at a pinned commit under
`tests/Ucp.Tests/ucp-schemas`. That includes every product in the shipped
catalogue, in both languages, against a real database.

### UCP's checkout binding — an agent's token required

Phase 11 (ADR 0039). The five tools UCP defines for a checkout session, over the
same operations as the REST binding at `/ucp/checkout-sessions`. **The one part of
`/mcp` that is not anonymous**: each tool refuses a caller without an agent's
bearer token, and a session belongs to the agent that opened it.

| Tool | What it does |
|---|---|
| `create_checkout` | Opens a session for line items (`item.id` is a variant SKU from the catalogue tools) |
| `get_checkout` | The session as it stands: status, totals, messages, fulfillment options |
| `update_checkout` | Replaces the whole checkout — buyer, destination, chosen option, discount codes |
| `complete_checkout` | Places the order with one instrument from a handler the checkout offered; needs `meta.idempotency-key` |
| `cancel_checkout` | Cancels an open session; needs `meta.idempotency-key` |

Every call carries `meta.ucp-agent.profile`: the shop fetches that profile to
negotiate, **only from a trusted host** (`Ucp:TrustedPlatformHosts`; Development
trusts `localhost`, where the API publishes its seeded agents' profiles at
`/dev-platforms/{agent}/profile.json`).

**What an agent can settle.** A test credential (ADR 0038 — `claude-desktop-test`
in the development issuer) is offered the sandbox handler `com.elguerre.tendero.fake`:
complete with `{"payment":{"instruments":[{"id":"i1","handler_id":"tendero_fake_1","type":"card","credential":{"type":"com.elguerre.tendero.fake_token","token":"card-ok"}}]}}`
and the order is a rehearsal, ended before it ships. A live agent (`claude-desktop`)
is offered no handler: its checkout reaches `requires_escalation`, and its
`continue_url` hands the basket to a person on the storefront.

**With AP2's mandates** (ADR 0040). The seeded profiles speak
`dev.ucp.common.payment.ap2_mandate`, so every checkout comes back with
`ap2.merchant_authorization` — the shop's signature, verifiable with the key under
`keys` in `/.well-known/ucp` — and completion requires `ap2.checkout_mandate`. In
Development the API plays the platform: POST the checkout it answered to
`/dev-platforms/{agent}/checkout-mandates` and it returns a mandate signed with the
key its profile publishes, which goes into the complete request as
`"ap2":{"checkout_mandate":"…"}`. A profile URL ending in `?ap2=false` is the same
agent without AP2: nothing is signed and no mandate is asked for.

Over REST, the same flow:

```bash
TOKEN=$(curl -s -X POST http://localhost:5130/dev-issuer/connect/token   -d grant_type=password -d username=claude-desktop-test | jq -r .access_token)
curl -s -X POST http://localhost:5130/ucp/checkout-sessions   -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json'   -H 'UCP-Agent: profile="http://localhost:5130/dev-platforms/claude-desktop-test/profile.json"'   -d '{"line_items":[{"item":{"id":"B11COO0301-DEFAULT"},"quantity":1}]}'
```

### Tendero's own

Answers UCP has no capability for, in this shop's own shapes.

| Tool | What it does |
|---|---|
| `search_products` | The shop's search, returning its own result page — with `answer`: `match` (`Exact` or `Related`) and `helpTopic`, and `facets`: what the results can be narrowed by. Takes the storefront's filters and order (see below) |
| `get_product_detail` | The shop's product view — richer than UCP's for a person |
| `list_categories` | The category tree, named in the answered language |
| `list_offers` | The promotions running right now |
| `get_return_policy` | How many days to send something back, from delivery |
| `get_product_specs` | One product's specs, each value with its source — `catalogue`, or `description` with the exact words a person approved and how well they support it (phase 10) |
| `compare_products` | Two to four products aligned row by row; a cell whose source is `none` is *no data*, never a guess (phase 10) |

The last two waited for phase 10 on purpose: a comparison that cannot say "no
data" with authority is a comparison that guesses (ADR 0018). They are one query
— the one behind the product page's specs table and the storefront's comparison —
so an agent and a shopper can never be told different things.

**`search_products` filters as the storefront does** (R-7.3, F-5). Its
arguments, in the record's camelCase: `brands`, `within` (category codes, for a
search), `facets` (`CODE:VALUE` pairs such as `COLOR:BLACK`), `minPrice`,
`maxPrice`, `onOffer`, `newArrivals`, `inStock`, and `sort` — `Newest`,
`PriceAsc` or `PriceDesc`, unset for `Relevance`. Within a filter the choices
add up; across filters they narrow. The answer's `facets` say what to send: each
facet's `code`, each option's `value` and the `count` of products it would leave,
so an agent narrows from the answer it already holds, never from a label it
translated.

```bash
curl -sS -X POST http://localhost:5130/mcp   -H 'Content-Type: application/json'   -H 'Accept: application/json, text/event-stream'   -d '{"jsonrpc":"2.0","id":3,"method":"tools/call",
       "params":{"name":"search_products",
                 "arguments":{"category":"SHOES","brands":["Vertex"],
                              "facets":["COLOR:BLACK"],"maxPrice":100,"sort":"PriceAsc"}}}'
```

`get_product_detail` was called `get_product` until the UCP binding arrived and
took that name. Two tools cannot share one on a server; a name we chose yields to
a name we were given.

**A closed set is spelled as the shop's API spells it: by its C# name**
(ADR 0043). A product's `status` is `Active` or `Draft`, an attribute's `kind` is
`LocalizedText`, a spec's `kind` the same. Until 2026-09-28 these tools said
`active` and `localizedtext`; an agent that matched those strings should match
the new ones. A spec's `source` is unchanged — `catalogue`, `description` or
`none` — because it is a set of constants UCP's knowledge extension publishes
too, not an enum. The UCP tools spell their values as UCP says, and did not
change.

## Try the refusals first

A system that says yes is a demo like any other. These need no catalogue data:

```bash
# A one-character query is refused, and says why.
curl -sS -X POST http://localhost:5130/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call",
       "params":{"name":"search_products","arguments":{"query":"z"}}}'

# Agent metadata that is present but malformed is refused by member name.
curl -sS -X POST http://localhost:5130/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call",
       "params":{"name":"lookup_catalog",
                 "arguments":{"ids":[],"meta":{"ucp-agent":{}}}}}'

```

A batch beyond fifty identifiers is refused by name and number too; that one is
easier to try from a script than from a shell.

Both come back as `isError` with the sentence in them, rather than a stack trace.
That is the transport's job: over HTTP the same refusal is a ProblemDetails.

## What it will not do

**Nothing writes.** There is no cart, no checkout and no order tool here, and
that is not an oversight — it is the trust model. An agent reaching this server
is an external process with no principal of its own, so it may read a catalogue
that is public on the web anyway. Writing needs an agent identity and a signed
mandate, which is phase 11.

The profile at `/.well-known/ucp` says the same thing in machine-readable form:
two catalogue capabilities, one MCP binding, and an empty `payment_handlers` —
this shop settles no payments over UCP yet.

## Three agent surfaces, and this is one of them

| Surface | The agent is | How it is authorized |
|---|---|---|
| **WebMCP** | code in the shopper's own browser tab | inherits their session |
| **MCP server** (this page) | an external process | anonymous, read-only |
| **UCP + AP2** | a principal of its own, server to server | bearer token plus a signed mandate |

What separates them is the trust model, not the transport.

The browser's tools (`frontend/apps/storefront/src/app/agent/storefront-tools.ts`)
filter too, and differently on purpose: `list_filters` reads the facets of the
list the shopper has on screen and `apply_filter` narrows THAT list, by one
facet at a time, as a click would — the shopper watches it change and the back
button undoes it. An external process has no screen to change, so this server's
`search_products` takes the filters as arguments instead.
