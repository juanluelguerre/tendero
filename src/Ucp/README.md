# Ucp

The `/.well-known/ucp` manifest: what this shop can do, in a document a machine
reads without being told anything in advance.

**This was a placeholder until 2026-09-08**, and its own text explained why: a
project with no code is one more assembly for the architecture rules to exempt.
It holds code now — the shape of the document, taken from the specification
rather than invented — and it joined `Solution.Transports` in the same commit,
so the two rules that constrain `src/Mcp` constrain this from its first line
rather than from whenever somebody remembers.

## What is here, and what is not

| | |
|---|---|
| **Here** | the document's shape, the capability registry, the catalogue MCP binding's own contracts, and `GET /.well-known/ucp` composed from the registry |
| **Phase 11** | the transactional capabilities, real `payment_handlers`, and `keys` once something is signed |

**One claim this file used to make was wrong, and the specification's own JSON
Schema said so.** It read: *the two absent sections are absent rather than empty
on purpose — a shop publishing an empty payment-handler map is advertising a
checkout it cannot honour.* Neither half survives. The signing-key field is
`keys`, a JWK Set, not `signing_keys`; and a **business** profile is *required*
to carry `payment_handlers`, so omitting it makes the document invalid rather
than modest. An empty map does not advertise a checkout — it says this shop
settles no payments over UCP, which is exactly true until phase 11.

`keys` stays absent, because that one really is optional and there is nothing to
publish until something is signed.

## The version is pinned, and no conformance is claimed

`2026-04-08`, read from [the specification](https://ucp.dev/2026-04-08/specification/overview/)
on 2026-09-08. UCP versions are dates; a shop advertising "whatever the latest
is" would be promising a document nobody has read it against, so moving that
string is a deliberate act with a diff.

What is asserted is that the profile has the shape the specification describes
for the capabilities Tendero actually serves. Nothing is claimed about passing a
certification, which does not exist to pass.

Two more corrections the schemas made to what had been assumed here: a
capability's value in the registry is an **array**, the same shape as a service's,
and a business profile's capability requires its `schema` — the canonical URL a
platform fetches to compose it, which is the protocol's document and not one of
ours.

One correction the specification made to this repository's own notes:
`docs/analysis/roadmap.md` wrote the identity capability as `dev.ucp.identity`,
and it is **`dev.ucp.common.identity_linking`**. The two catalogue ids that
roadmap guessed — `dev.ucp.shopping.catalog.search` and
`dev.ucp.shopping.catalog.lookup` — are exactly right, which is the more useful
half of checking.
