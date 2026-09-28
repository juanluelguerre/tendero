# ADR 0043 — A closed set is declared in the contract, spelled as its C# name

Status: accepted · 2026-09-28 · the author chose the spelling · Related: [ADR 0030](0030-protocol-routes-are-not-in-the-openapi-document.md) · invariant 11 (`CLAUDE.md`) · Found by: the redesign's baseline walkthrough, W-1 ([`redesign.md`](../specs/redesign.md))

## Context

The walkthrough suspended an agent, and the backoffice went on showing it as
*Active*. The API said `suspended` and the template compared with
`'Suspended'`. Nothing complained, because the OpenAPI document said the field
was a `string`, and so did the TypeScript type generated from it. A day earlier
R-4.3 had fixed the same mismatch in the agents' trust select, one symptom at a
time, and the redesign had carried the status comparison into six more places.

It was a class of bug, not an instance. Measured on 2026-09-28, the API wrote
its enums in **four spellings**, with no rule deciding between them:
- the member name (`Confirmed`, from `ToString()`);
- lower case (`active`, `localizedtext`, from `ToLowerInvariant()`);
- the metric label (`payment_declined`, from `MetricLabels.Of`);
- camel case (`alreadyDraft`, from `JsonNamingPolicy.CamelCase`).

The same enum was sometimes spelled two ways by two endpoints: `AttributeKind`
was `LocalizedText` in the definitions and `localizedtext` on a product. None of
it reached the contract, which declared no `enum` at all.

## Decision

1. **A field whose values are a closed set is typed as its enum**, in request and
   response records alike. The slice passes the value and never spells it:
   no `ToString()`, `ToLowerInvariant()` or `MetricLabels.Of` builds a wire value.
2. **One spelling for the whole API: the C# member name** (`PaymentDeclined`),
   written by one `JsonStringEnumConverter` registered in the API's JSON options,
   with integer values refused. It is .NET's own default. The frontends already
   assumed it: their translation keys (`status.Confirmed`, `trust.Known`) and
   their comparisons were written against it, and both bugs were places where
   the server had strayed from it. The cost is plain: the values are PascalCase
   while the property names are camelCase, as in Azure's `provisioningState:
   "Succeeded"`.
3. **The document declares every such set**, one `enum` schema per type, so the
   generated TypeScript types are unions (`'Active' | 'Suspended'`). A comparison
   with a value that does not exist stops compiling.
4. **A test holds it.** Any property of the document named like a closed set
   (`status`, `kind`, `mode`, `outcome`, `trust`, `decision` and their kin) that
   is a bare `string` fails `OpenApiDocumentTests`.
5. **Closed today, open tomorrow.** Adding a member is a contract change a
   client may not know yet, so a client never assumes the list is complete: a
   `switch` over a set ends in a branch for a value it does not know, and a label
   built from one falls back to the value itself. Removing or renaming a member
   is a breaking change, and is made as one. The rule is Microsoft Graph's
   *evolvable enums* and Zalando's *extensible enums*, without their machinery:
   at laboratory scale the clients are ours and ship with the API.
6. **What is not a closed set stays a string**: a free-text reason, a connector
   key chosen by configuration, a stop code the domain defines as constants, a
   culture, a currency.

## Consequences

- Values change on the wire where they were not PascalCase before (`active` →
  `Active`, `payment_declined` → `PaymentDeclined`, `alreadyDraft` →
  `AlreadyDraft`). Both frontends are ours and are generated from the document,
  so the compiler finds every comparison; what it cannot find — a translation key
  built at run time, an end-to-end test, TrafficSim reading raw JSON — was
  searched for by hand in the same change.
- Renaming an enum member is now a contract change. It always was; now the
  document diff says so.
- **UCP is not affected.** Its records carry no enum: every value it sends is a
  constant its specification spells (`ready_for_complete`), and it is outside
  the document (ADR 0030). Its knowledge extension's `source` reads
  `SpecSources`' string constants, which stay strings, so the extension's
  published schema does not move.
- **MCP's own tools change with the API**, because they return the same records
  (`ProductDetail`, `SpecSheet`, `ListOffersResult`). The MCP SDK serialises
  with its own options and not ASP.NET's; measured on 2.2.0, they already carry
  a `JsonStringEnumConverter` with no naming policy, so a tool answers
  `"Active"` exactly as the endpoint does. That is a default of somebody else's
  package, so the MCP server is given the API's converter explicitly rather than
  trusting it, and a test reads a tool's answer. An agent that had scripted
  against `"active"` sees `"Active"`: the change is written in `docs/mcp.md`.
- Telemetry keeps its own vocabulary: a metric label stays `payment_declined`,
  held by the tracking plan and `TrackingPlanTests`.
