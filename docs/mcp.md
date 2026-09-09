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

## What the server offers

Eight tools, and they are two different contracts on one server.

### UCP's catalogue binding

The shapes the specification fixes, for an agent that speaks UCP. `meta` must
carry `ucp-agent.profile` on every call; money is in **minor units** (39,00 € is
`3900`); a search that finds nothing is a `messages` entry, not an error.

| Tool | What it does |
|---|---|
| `search_catalog` | Free-text discovery, with `filters.category` and `pagination` |
| `lookup_catalog` | Resolve up to 50 identifiers at once |
| `get_product` | One product, with every option and every variant |

### Tendero's own

Answers UCP has no capability for, in this shop's own shapes.

| Tool | What it does |
|---|---|
| `search_products` | The shop's search, returning its own result page |
| `get_product_detail` | The shop's product view — richer than UCP's for a person |
| `list_categories` | The category tree, named in the answered language |
| `list_offers` | The promotions running right now |
| `get_return_policy` | How many days to send something back, from delivery |

`get_product_detail` was called `get_product` until the UCP binding arrived and
took that name. Two tools cannot share one on a server; a name we chose yields to
a name we were given.

## Try the refusals first

A system that says yes is a demo like any other. These need no catalogue data:

```bash
# A one-character query is refused, and says why.
curl -sS -X POST http://localhost:5130/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call",
       "params":{"name":"search_products","arguments":{"query":"z"}}}'

# A UCP call with no agent profile is refused by name.
curl -sS -X POST http://localhost:5130/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call",
       "params":{"name":"search_catalog","arguments":{"query":"mochila","meta":{}}}}'
```

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
