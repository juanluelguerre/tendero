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
| **Here** | `UcpSpecification` (the pinned version and the fixed path) and `UcpProfile` (the document's shape) |
| **`P9-6`** | `IUcpCapability` and a registry discovered by assembly scanning, each capability naming its slices by referencing their query types |
| **`P9-7`** | `GET /.well-known/ucp`, derived from that registry, with a snapshot test |
| **Phase 11** | the transactional capabilities, `payment_handlers` and `signing_keys` |

The two absent sections are absent rather than empty **on purpose**. A shop
publishing an empty payment-handler map is advertising a checkout it cannot
honour, which is worse than saying nothing at all.

## The version is pinned, and no conformance is claimed

`2026-04-08`, read from [the specification](https://ucp.dev/2026-04-08/specification/overview/)
on 2026-09-08. UCP versions are dates; a shop advertising "whatever the latest
is" would be promising a document nobody has read it against, so moving that
string is a deliberate act with a diff.

What is asserted is that the profile has the shape the specification describes
for the capabilities Tendero actually serves. Nothing is claimed about passing a
certification, which does not exist to pass.

One correction the specification made to this repository's own notes:
`docs/analysis/roadmap.md` wrote the identity capability as `dev.ucp.identity`,
and it is **`dev.ucp.common.identity_linking`**. The two catalogue ids that
roadmap guessed — `dev.ucp.shopping.catalog.search` and
`dev.ucp.shopping.catalog.lookup` — are exactly right, which is the more useful
half of checking.
