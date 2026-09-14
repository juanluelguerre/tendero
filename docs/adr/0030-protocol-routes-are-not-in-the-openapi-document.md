# ADR 0030 — A route whose protocol publishes its own discovery is not in the OpenAPI document

**Status:** accepted · 2026-09-09

## Context

Tendero now serves four kinds of HTTP route:

| | Contract published by |
|---|---|
| `/api/**` | `docs/openapi/tendero.json`, generated from the running API |
| `/dev-issuer/**` | the OIDC discovery document and JWKS |
| `/mcp` | MCP's own `tools/list` |
| `/.well-known/ucp` and `/ucp/**` | the UCP profile, and UCP's published JSON Schemas |

The last three have been excluded from the OpenAPI document one at a time, each
with a comment explaining itself, and each explanation slightly different. That
is a habit, not a rule, and the fourth case would have been decided by instinct.

The question that forced this: **UCP's REST binding is REST.** Unlike `/mcp` it
is an ordinary POST with a JSON body, so the reflex is that of course it belongs
in the API's own description. Two arguments were offered against and only one of
them was any good.

**The bad argument was that it is "internal".** It is not. Excluding a route from
the generated document removes it from a *description*, not from the API: the
endpoint stays public, anonymous and callable by anybody with `curl`. Anyone
reasoning from "internal" is reasoning from something false.

## Decision

**A route whose own protocol publishes a machine-readable description of it is
described there, and not a second time in `docs/openapi/tendero.json`.**

The reason is the one this repository applies everywhere else: **one source, not
two copies.** These routes already have an authoritative description —

- an OIDC client reads `/.well-known/openid-configuration`; it does not look for
  the token endpoint in our Swagger;
- an MCP client reads `tools/list`;
- a UCP agent reads `/.well-known/ucp`, and the payload shapes are defined by
  UCP's published JSON Schemas at `ucp.dev/schemas/**`, which this repository
  vendors and validates against.

A description of ours would be the **non-authoritative** copy of each of those.
When the two disagree, the other one wins — so the only thing our copy can add is
a way to be wrong.

There is a second, smaller reason and it is not the deciding one:
`docs/openapi/tendero.json` generates the TypeScript types both Angular apps
consume. Including the binding would put `UcpProduct`, `UcpPagination` and
`UcpInputCorrelation` into `schema.ts`, where no frontend has any use for them.

**The rule was already written down as a test**, before this question came up:

```csharp
Assert.All(paths, path => Assert.StartsWith("/api/", path, StringComparison.Ordinal));
```

*Every documented path is under `/api/`.* That test predates UCP and encodes
exactly this boundary; this ADR is the reasoning it was standing on.

## What was rejected

**Including them.** The honest argument for it is real: a gateway, a security
scanner or a docs generator reading our document will not see these routes. That
is a cost, and it is accepted rather than waved away — the answer for a human is
the route table in `architecture.md`, which lists everything, and the answer for a
machine is the discovery document its own protocol tells it to read.

**A second OpenAPI document scoped to the UCP binding.** This is what a project
serving several distinct surfaces would normally do, and it avoids the frontend
types problem entirely. It is rejected for the same reason as the first option
rather than for cost: it would still be a third description of something that
already has two, and ours would still be the one that can go stale. UCP's profile
even has a `schema` field for a binding, and what belongs in it is
`ucp.dev`'s URL, not ours.

## Consequences

Every route added under a protocol prefix — a future A2A binding, an embedded
one, a second identity provider — is excluded by this rule rather than by a
judgement call, and the `/api/` assertion enforces it without anyone remembering.

`architecture.md`'s route table becomes the single place a person can see the
whole HTTP surface, which makes keeping it current a real obligation rather than
a courtesy.

And the general form, which is worth more than the specific case: **when
somebody else's protocol already describes your endpoint, your job is to point at
their description, not to write a rival one.**
