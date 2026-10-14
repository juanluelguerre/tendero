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

### "Not authenticated", and why Authenticate does not help

`claude mcp list` (or `/mcp` inside a session) shows the server as connected and
**not authenticated**, and that is the correct state: everything on `/mcp` but
UCP's five checkout tools is anonymous. **Do not press Authenticate.** It starts
an OAuth flow with dynamic client registration, and neither the development
issuer nor the shop offers one — invariant 10: Tendero validates tokens and is
never an authorization server. Finding no registration endpoint, the client
falls back to `/register` on the server's own origin, and the answer is a 404
from whatever serves that origin (Vite's dev server, through `localhost:4200`).

For the checkout tools, give the client an agent's token as a header — the same
token the REST example below uses, from the development issuer:

```bash
TOKEN=$(curl -s -X POST http://localhost:5130/dev-issuer/connect/token   -d grant_type=password -d username=claude-desktop-test | jq -r .access_token)
claude mcp add --transport http tendero http://localhost:5130/mcp   -H "Authorization: Bearer $TOKEN"
```

The development issuer mints its signing key per process, so a restarted API
refuses yesterday's token: add the server again with a fresh one.

**On the 2026-07-28 protocol** a client lists the tools without a handshake, and
the MCP SDK then infers each tool's `outputSchema` from its return type. The
checkout tools return JSON (a UCP checkout, or UCP's error response), whose
inferred schema is `true` — not an object schema — and Claude Code refused the
whole list over it ("expected object, received boolean"). They declare an
object now, and `McpTransportTests` asks `tools/list` the way that protocol does.

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
| **Gets back** | this shop's own shapes, in `result.structuredContent` under a declared `outputSchema`, and the same JSON as text in `result.content` | the UCP envelope — its `ucp` member is what tells it apart — in `result.structuredContent` |
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

**Every tool says what it does to the shop** (since 2026-10-13, phase 13b): the
twelve that only read carry `readOnlyHint`, and the checkout's four that change
something say whether it can be taken back — completing and cancelling cannot.
Left unsaid, the specification's defaults announce a tool as destructive, and a
careful host asks a person before every search.

**The endpoint has two guards the specification requires of a server**: an
`Origin` it does not know is answered 403 — a request with none, as an agent
that is not a browser sends, is served — and a client is limited to 120 calls a
minute, after which it is answered 429.

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

### An agent of your own, on your own machine — nothing to publish

Everything above runs on `localhost` as it comes, and so does an agent you
write yourself. **Nothing has to be published on the internet to try the shop
locally.** What a local agent needs:

| What | Where it comes from locally |
|---|---|
| A token that says it is an agent | The development issuer: `claude-desktop-test` (test orders) or `claude-desktop`, at `/dev-issuer/connect/token`. An agent nobody registered buys too, as untrusted, under stricter limits; register it in the backoffice under *Agentes* to vouch for it |
| A platform profile the shop will fetch | Any URL on `localhost`, on any port: Development trusts that host (`Ucp:TrustedPlatformHosts`). Use the seeded one — `/dev-platforms/{agent}/profile.json` — or serve your own from your agent's process |
| A way to settle | The sandbox handler `com.elguerre.tendero.fake`, offered to a test credential |

**The one thing a local shop cannot do**, and it stops nothing: have its *own*
extension accepted by a platform that follows UCP to the letter. The shop
declares `com.elguerre.tendero.knowledge` beside UCP's standard capabilities,
and UCP's **authority binding** says a platform accepts a capability only when
its schema is served from a host its name reverses to — here, one under
`elguerre.com`, over `https`. That is how a name proves whose it is: without
it anybody could declare capabilities in somebody else's name. `localhost` is
nobody's domain, so such a platform treats that one extension as absent and
goes on with the rest: discovery, the catalogue, checkout and AP2 are all
`dev.ucp.*` and bind from `ucp.dev`. An agent of your own that does not check
the binding sees the extension as well.

`Ucp:OwnSchemasUrl` is where those schemas are published, and it is **empty on
purpose**: unset, they are served by the shop itself, wherever it runs. Set it
only when the shop is deployed for agents you do not control — to an `https`
origin under `elguerre.com` that serves, at `/ucp/schemas/…`, the files in
`src/Ucp/Schemas/` — and the shop refuses to start if the address could not
bind (`UcpAuthority`). It is a deployment step, not a development one.

### Tendero's own

Answers UCP has no capability for, in this shop's own shapes.

| Tool | What it does |
|---|---|
| `search_products` | The shop's search, returning its own result page — with `answer`: `match` (`Exact` or `Related`) and `helpTopic`, and `facets`: what the results can be narrowed by. Takes the storefront's filters and order (see below) |
| `get_product_detail` | The shop's product view — richer than UCP's for a person |
| `list_categories` | The category tree, named in the answered language |
| `list_offers` | The promotions running right now |
| `list_campaigns` | The shop's campaigns running now or starting within 30 days, leading one first — name, line, kind (`Event`/`Season`), scope (`Shop`/`Category`), phase (`Running`/`Upcoming`), window and what each holds (R-8, H-5) |
| `get_return_policy` | How many days to send something back, from delivery |
| `get_product_specs` | One product's specs, each value with its source — `catalogue`, or `description` with the exact words a person approved and how well they support it (phase 10) |
| `compare_products` | Two to four products aligned row by row; a cell whose source is `none` is *no data*, never a guess (phase 10) |
| `list_kits` | The kits the shop puts together to a budget: each one's code, name, slots (needed or optional) and requirements as `CODE:VALUE` — `GET /api/kits` (phase 13c) |
| `build_kit` | One kit solved for a budget in minor units, with requirements from `list_kits`: the total, what is left, what held it back, each slot with its product or why it is empty, and the shop's sentences — the page's query, `POST /api/kits/{template}/build`. A kit the shop does not have is refused as `KitNotFound`, naming it (phase 13c, K-7) |

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
translated. **A price goes back in the unit it came in**: `minPrice` and
`maxPrice` are whole minor units, as every price in the answer is — `10000` is
100.00 EUR, and the price facet's `min` and `max` can be sent as they are. A
fraction is refused by name.

```bash
curl -sS -X POST http://localhost:5130/mcp   -H 'Content-Type: application/json'   -H 'Accept: application/json, text/event-stream'   -d '{"jsonrpc":"2.0","id":3,"method":"tools/call",
       "params":{"name":"search_products",
                 "arguments":{"category":"SHOES","brands":["Vertex"],
                              "facets":["COLOR:BLACK"],"maxPrice":10000,"sort":"PriceAsc"}}}'
```

**`list_campaigns` is `GET /api/pricing/campaigns`**, the same query and the same
answer (R-8, H-5): an agent asked "are there deals this week?" reads what the
home's campaign card is drawn from instead of scraping it. Its one argument is
`culture`. The window is data — `startsAt` inclusive, `endsAt` exclusive — so an
agent computes "until when" itself, as the card's clock does; a running
campaign's products are `search_products` with its code as `campaign`. An empty
`items` means no campaign is near.

```bash
curl -sS -X POST http://localhost:5130/mcp   -H 'Content-Type: application/json'   -H 'Accept: application/json, text/event-stream'   -d '{"jsonrpc":"2.0","id":4,"method":"tools/call",
       "params":{"name":"list_campaigns","arguments":{"culture":"en"}}}'
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

Both come back as `isError`, rather than a stack trace. That is the transport's
job: over HTTP the same refusal is a problem (RFC 9457).

**The shop's own tools refuse with that same problem** (since 2026-10-13, phase
13b; ADR 0059): the text of the error is JSON and nothing around it — a `code`
to branch on, the shop's `title` and `detail` in the language asked, and for a
request that did not validate, `errors`: each argument to what is wrong with it.

```json
{"code":"Invalid","title":"La petición no es válida.","errors":{"Text":["…at least 2 characters…"]}}
{"code":"ProductNotFound","title":"Ese producto no existe en la tienda.","detail":"No hay ningún producto con el código «NOPE»."}
```

**An argument left out or misnamed is told which**, on every tool: `Invalid`
again, with `detail` naming the argument and the binder's own sentence as
`reason` — not the SDK's *An error occurred invoking…*, which is what a tool
that broke says and tells an agent nothing it can correct.

```json
{"code":"Invalid","title":"La petición no es válida.","detail":"Falta el argumento «code».","reason":"The arguments dictionary is missing a value for the required parameter 'code'."}
```

UCP's tools keep UCP's own wording, because the refusal is the protocol's to
spell — and its **protocol** errors (no profile, a profile on a host nobody
trusts, another protocol version) are JSON-RPC errors and not tool results:
`-32001` for discovery, `-32000` for the rest, with UCP's `code` and `content`
in `error.data`.

**Who may call a tool is HTTP's to say** (MCP, Authorization; RFC 9728). The
endpoint is open and the five checkout tools are not, so the door is decided
per call, before the server is reached:

```bash
# No token on a checkout tool: 401, and where to get one.
curl -sS -i -X POST http://localhost:5130/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call",
       "params":{"name":"get_checkout","arguments":{"id":"x","meta":{}}}}'
# HTTP/1.1 401 Unauthorized
# WWW-Authenticate: Bearer resource_metadata="http://localhost:5130/.well-known/oauth-protected-resource/mcp"

curl -sS http://localhost:5130/.well-known/oauth-protected-resource/mcp
# {"resource":"http://localhost:5130/mcp","authorization_servers":["…the issuer…"],"bearer_methods_supported":["header"],…}
```

A person's token on one of those tools is a 403; a token that is not good is a
401 with `error="invalid_token"` on any call, even one that needed none. What
reads the catalogue, and asking what the server offers, ask for nothing.

**The 2026-07-28 revision's own questions are answered** — `server/discover`
with the versions spoken, a version not spoken with `-32022` and the ones that
are, and a tool list with its caching hints: `cacheScope: "public"` (the list
is the same for whoever asks) and `ttlMs` of five minutes.

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

### Seeing the browser's tools: a flag, DevTools, and an inspector

WebMCP is **native to the browser** — no extension is needed for the tools to
exist — and it is behind a flag. Tried on 2026-10-13 in Edge 155; the flag is
the same in Chrome.

1. **Turn it on**: `edge://flags/#enable-webmcp-testing` or
   `chrome://flags/#enable-webmcp-testing` (*WebMCP for testing*), then
   **relaunch the browser** — reloading the tab is not enough.
2. **Open the storefront** (`http://localhost:4200`; `localhost` is a secure
   context, which the API requires). The shell registers seven tools as the
   page starts, and the header's search form is an eighth by itself (below).
   In the console, `document.modelContext` is an object, and
   `await document.modelContext.getTools()` lists them.
3. **DevTools → Application → WebMCP** shows them without any extension:
   *Available Tools*, each with the description the shop wrote for a model,
   and *Tool Activity*, every call with its status and — selected — its input.
   A call the shop refuses reads *Completed* there: the browser reports that
   the tool answered, and the refusal is in the answer, as `isError`.

**To call a tool by hand**, the extension **WebMCP - Model Context Tool
Inspector** adds a side panel: the tools with their input schemas and
annotations, a tool to pick, its arguments as JSON pre-filled from the schema,
and *Execute Tool*, which shows what came back — for instance, on the home
page, where there is no list to narrow:

```json
{"content":[{"type":"text","text":"There is no product list on screen. Filters narrow a search or category page the shopper is on."}],"isError":true}
```

It is listed among the developer utilities of `GoogleChromeLabs/webmcp-tools`
(which says of itself that it is not an officially supported Google product)
and its source is `beaufortfrancois/model-context-tool-inspector`; it is in the
Chrome Web Store, and installs in Edge too:
<https://chromewebstore.google.com/detail/webmcp-model-context-tool/gbpdfapgefenggkahomfgkhfehlcenpd>.
Two things to know before using it:

- **Executing a tool needs no key and sends nothing anywhere**: it is the
  browser calling the page. The panel's *User Prompt* box is another matter —
  it asks for a Gemini API key and sends the prompt and the page's tools to
  that service, which is not something the shop needs in order to be tried.
- **What a tool does, it does**: `add_to_cart` puts a line in the cart of the
  session the tab is in, exactly as the button would.

**Nobody asks before a tool runs** (checked 2026-10-14, Edge 155):
`search_products`, then `get_product`, then `add_to_cart`, called as an agent
would. The line was in the cart in 69 ms with no prompt from the browser or
the shop. Edge reports each tool's annotations, `consequentialHint` among them,
and the shop's are all `false`. Whether to ask first is the calling agent's
choice.

**The browser's own agent does not use them yet.** Copilot in Edge does not
see a page's WebMCP tools, and no flag turns that on. In
`MicrosoftEdge/webmcp-labs` issue #17 (opened 2026-10-06, still open), a
Microsoft engineer answers that there is no public way to test WebMCP with
Copilot in Edge, the drawer included, and that the testing flag does not enable
it. **To see a model drive the shop's tools today**, the same repository has
**WebMCP Explorer**. It is an extension loaded unpacked from
`webmcp-explorer/dist/`, with an agent loop over Azure OpenAI, OpenAI,
Anthropic or any Chat Completions endpoint, a local one included (Ollama at
`http://localhost:11434/v1`, no key). The model must call tools. It has not
been tried with the shop yet. **For the future**: try it with a local model,
and check again whether Copilot has started reading the page's tools. When
it does, a capture of Copilot driving Tendero belongs in the article on the
three agent surfaces.

### Where each of the browser's tools is declared

Eight tools, declared in **two different ways**, which is why a search of the
templates for `toolname` finds only one of them:

| Tool | What it does | Only reads | How it is declared | Where |
|---|---|---|---|---|
| `search_products` | Finds products and tells the agent | yes | script | `storefront-tools.ts`, `searchProducts()` |
| `get_product` | One product, with every variant and its SKU | yes | script | `storefront-tools.ts`, `getProduct()` |
| `list_filters` | What the list on screen can be narrowed by | yes | script | `storefront-tools.ts`, `listFilters()` |
| `apply_filter` | Narrows the list the shopper is looking at | no | script | `storefront-tools.ts`, `applyFilter()` |
| `add_to_cart` | Puts one SKU in the shopper's own cart | no | script | `storefront-tools.ts`, `addToCart()` |
| `get_cart` | The cart, its total and its discounts | yes | script | `storefront-tools.ts`, `getCart()` |
| `list_campaigns` | The campaigns running or about to | yes | script | `storefront-tools.ts`, `listCampaigns()` |
| `show_search_results` | Sends the shopper's page to a search's results | — | **markup** (experimental) | `shell/shell.html`, the header's `<form>`; `agent/search-form-tool.ts` |

All under `frontend/apps/storefront/src/app/`.

**From script — the seven, the way the draft defines.** Nothing of them is in
any template. Each is a method of `StorefrontTools` (`agent/storefront-tools.ts`)
that returns the tool as an object — its name, a description written for a
model, an input schema, its annotations and the `execute` that runs it — and
`all()` returns the seven. Three files, one step each:

1. **Declared** — `agent/storefront-tools.ts`: what each tool is.
2. **Registered** — `frontend/libs/shared/agent/src/lib/agent-tools.ts`,
   `AgentTools.register`: one `document.modelContext.registerTool(…)` a tool,
   all under one signal whose abort takes them back. Shared by both apps; the
   tools themselves are the storefront's.
3. **Started** — `shell/shell.ts`, in the constructor:
   `this.agent.register(this.tools.all());` — once, with the page.

**From markup — the eighth.** It is not in `storefront-tools.ts` and nothing
registers it: the browser makes it from the form's attributes. The next
section is about it.

### The eighth tool, declared in the markup — experimental

Seven of the tools are registered from script, the way the draft defines
(`document.modelContext.registerTool`). The eighth, **`show_search_results`**,
is not registered by anything: it is the header's search form, with attributes.

```html
<form role="search"
      toolname="show_search_results"
      tooldescription="Shows the shopper the results page for what they are looking for: …"
      toolautosubmit>
  <input type="search" name="q"
         toolparamdescription="What to look for, in the language the page is in. At least two characters.">
</form>
```

The browser makes the tool from the form: its name and description from the
form's attributes, its input schema from the controls (`q`, a string, with the
description beside it), and — because of `toolautosubmit` — it fills the box
and sends the form itself. The page then does what it does when a person
presses Enter, and answers the agent through the `submit` event, which gains
`agentInvoked` and `respondWith()`:

```ts
// apps/storefront/src/app/agent/search-form-tool.ts
export function answerAgent(event: SubmitEvent, words: string, go: (query: string) => Promise<boolean>): boolean {
  const sent = event as SubmitEvent & AgentSubmit;
  if (!sent.agentInvoked || typeof sent.respondWith !== 'function') return false;   // a person: search as always

  const query = words.trim();
  sent.respondWith(
    query.length < 2
      ? Promise.resolve('A query needs at least two characters.')
      : go(query).then(() => `The shopper is now looking at the results for "${query}".`),
  );
  return true;
}
```

**Why it is there, and what it is not.** It is the other way to declare a
tool, kept in the real shop so the two can be read side by side; and it does
something `search_products` does not — that one hands the agent a list and
leaves the page alone, this one **moves the shopper's page** to the results.

**It is not in the specification.** This is WebMCP's *declarative* proposal
(`declarative-api-explainer.md` in the Community Group's repository, whose
README says *being explored … not part of the specification at this time*).
It is in the browser all the same — tried in Edge 155 behind the flag on
2026-10-13: the form appears in `getTools()`, executing it navigated to
`?q=sartén` and came back with the sentence above, and one letter was refused
without moving the page. It may change or go, and it is marked experimental in
the template and in `search-form-tool.ts`. A browser that knows nothing of it
ignores the attributes, and the form is the form it was — pressing Enter in
it is unchanged.

Without the flag — every other browser, and this one as it comes — none of
this exists and the shop is the shop it was: `supportsModelContext()` is the
whole degradation (`frontend/libs/shared/agent/src/lib/model-context.ts`).
