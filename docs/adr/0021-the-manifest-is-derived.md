# ADR 0021 — The UCP manifest is derived from a capability registry

**Status:** accepted · 2026-09-08

## Context

`/.well-known/ucp` tells an agent what this shop can do. It is the document the
project's headline claim rests on — there is no .NET reference implementation of
UCP — so what it says has to be true, and stay true.

A hand-written JSON file at a fixed path is the obvious implementation and the
wrong one. It is correct on the day it is written and rots the first time a slice
moves, in silence, because nothing in a build compares a JSON file to the code it
describes. The failure is worse than a stale document: an agent reads a promise,
calls it, and gets nothing.

## Decision

**The manifest is composed from a capability registry, and a capability names its
slices by holding their types.**

```csharp
public sealed class CatalogSearchCapability : IUcpCapability
{
    public string Id => "dev.ucp.shopping.catalog.search";
    public string Schema => $"{UcpSpecification.SchemaBaseUrl}/shopping/catalog_search.json";
    public IReadOnlyList<Type> Slices => [typeof(SearchProductsQuery)];
}
```

`typeof(SearchProductsQuery)` and not `"SearchProducts"`. Deleting the slice does
not produce a manifest advertising a search that no longer exists; it produces
`error CS0246`. Capabilities are discovered by assembly scanning — the mechanism
`AddTenderoCqrs` already uses — so adding one is adding a class, and forgetting
to register one is not a failure mode that exists. They are ordered by id,
because a snapshot over a document whose keys move between runs is a coin toss.

Three things the compiler cannot see are tests instead:

- every named type really is an `IQuery<>` or an `ICommand<>`, because naming a
  plain record would compile and would advertise a verb with nothing behind it;
- the declared set is asserted exactly, so a third capability is deliberate;
- the served document is compared against a committed snapshot,
  `docs/ucp/well-known-ucp.json`, regenerated with `UPDATE_UCP=1`.

**Verify was declined**, and the reason is Respawn's. `docs/testing.md` has
prescribed it for this document since before the document existed, and the
argument — a derived manifest is only safe if a change shows up in a diff — is
correct. It is also already satisfied: `docs/openapi/tendero.json` is a committed
snapshot compared by a test with an environment variable to regenerate it. A
second snapshot mechanism for the second derived document would be two workflows
and two things to explain, for a guarantee the first one already gives.

## What a manifest may not do, and what "honest" cost

**Advertise what nobody can reach.** Every slice behind cart, discount,
fulfillment, checkout and orders exists and has since phase 5. What does not
exist is any way for an agent to be *authorised* — its own principal and a signed
mandate are phase 11 — so those capabilities are absent. A theory names the six
absent ids, so declaring them later is a deliberate act.

**Claim a binding it does not serve.** Composing the profile meant declaring a
service, and declaring one meant reading what a service binding requires. UCP's
catalogue MCP binding fixes three tool names, a required `meta`, a response
envelope and money in minor units. Tendero's tools were `search_products` and
`get_product` answering its own shapes — **so `/mcp` was not UCP's MCP binding**,
and pointing `dev.ucp.shopping` at it would have been a lie in the one document
whose entire value is being trustworthy. The binding was implemented before the
line declaring it was written.

**Guess a shape.** Three things this design got wrong by reasoning were corrected
by reading the specification's own JSON Schemas: the signing-key field is `keys`
and not `signing_keys`; a capability's registry value is an array, the same shape
as a service's; and a **business** profile requires `payment_handlers`, so
"absent rather than empty" — argued in three places on the grounds that an empty
map advertises a checkout we cannot honour — made the document invalid rather
than modest. An empty map advertises no payment handlers, which is the truth.

## Consequences

The manifest cannot describe a shop this process is not: it is composed per
request from the registry, and the endpoint URL comes from the request's own
host.

Capability `schema` URLs are the protocol's canonical ones, not ours. We declare
which contract we speak; we do not publish a contract we wrote and call it UCP.

**No conformance is claimed.** The version is a pinned constant, what is asserted
is the shape for the capabilities actually served, and the strongest remaining
evidence — validating the served document against UCP's published schemas —
needs a JSON Schema library. That is a dependency decision and it is recorded on
the board rather than taken quietly.
