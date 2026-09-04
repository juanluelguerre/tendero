# Architecture Decision Records

| # | Decision | Status |
|---|---|---|
| [0001](0001-vertical-slices.md) | Vertical slices with Carter + custom CQRS | accepted |
| [0002](0002-bounded-contexts-snapshot.md) | Two bounded contexts; orders snapshot products | accepted |
| [0003](0003-ports-keyed-adapters.md) | Ports + keyed adapters for connectors and payments | accepted |
| [0004](0004-lexical-fallback-per-language-indexes.md) | BM25 as permanent fallback; one index per language | accepted |
| [0005](0005-ai-layering-meai-maf.md) | MEAI for pipelines, MAF for agents, no Harness | accepted |
| [0006](0006-dependency-baseline.md) | Dependency and toolchain baseline (licences, preview pins) | accepted |
| [0007](0007-search-reads-catalog-domain.md) | Search reads the Catalog domain directly | accepted |
| [0008](0008-persistence-shape.md) | Persistence shape: jsonb for documents, tables for keys | accepted |
| [0009](0009-spec-driven-development-scope.md) | Lightweight specs now; Spec Kit as a phase-3 experiment | accepted |
| [0010](0010-frontend-sharing-boundaries.md) | What the two frontends share, and what they duplicate | accepted |
| [0011](0011-product-images.md) | Product images are ingested, not referenced | accepted |
| [0012](0012-publishing-and-reindexing.md) | Publishing is an explicit step; the index is rebuildable | accepted |
| [0013](0013-culture-negotiation.md) | Culture: explicit parameter wins, `Accept-Language` is the default | accepted |
| [0014](0014-context-map.md) | Contexts share no entities; the count is not the rule | accepted |
| [0015](0015-variant-is-the-indexed-unit.md) | The variant is the indexed unit; the product is the returned unit | accepted |
| [0016](0016-quoted-live-frozen-at-order.md) | Prices are quoted live and frozen at order time | accepted |
| [0017](0017-the-identity-provider-is-a-port.md) | The identity provider is a port; the first adapter was a development issuer | accepted |
| [0024](0024-the-outbox-is-the-process-manager.md) | The outbox is the process manager, orchestrated from Ordering | accepted |
| [0025](0025-authorise-before-placing.md) | Checkout authorises the payment before it places the order | accepted |
| [0026](0026-the-url-carries-a-code.md) | The URL carries a code; the slug is decoration | accepted |
| [0027](0027-audit-writes-outside-the-transaction.md) | The audit log writes outside the command's transaction | accepted |

Format: context → decision → consequences. Keep each under a page.

The gap between 0017 and 0024 is deliberate: `roadmap.md` reserves 0018-0023 for
decisions later phases are already committed to making (claims, the copilot,
MCP/UCP transports, the capability registry, agent principals, feature flags).
Renumbering them as they land would break the references the roadmap and the code
already carry. 0017 was the first of those reservations to be cashed, in phase 7.
