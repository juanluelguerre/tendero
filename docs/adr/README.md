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
| [0018](0018-the-ai-writes-claims-not-products.md) | The AI writes claims, not products | accepted |
| [0020](0020-transports-over-the-dispatcher.md) | MCP and UCP are transports over the dispatcher | accepted |
| [0021](0021-the-manifest-is-derived.md) | The UCP manifest is derived from a capability registry | accepted |
| [0022](0022-an-agent-is-a-principal-not-a-customer.md) | An agent is a principal, not a customer | accepted |
| [0023](0023-feature-flags-with-removal-conditions.md) | Feature flags are a closed set, each with a removal condition and one reader | accepted |
| [0024](0024-the-outbox-is-the-process-manager.md) | The outbox is the process manager, orchestrated from Ordering | accepted |
| [0025](0025-authorise-before-placing.md) | Checkout authorises the payment before it places the order | accepted |
| [0026](0026-the-url-carries-a-code.md) | The URL carries a code; the slug is decoration | accepted |
| [0027](0027-audit-writes-outside-the-transaction.md) | The audit log writes outside the command's transaction | accepted |
| [0028](0028-prices-include-vat.md) | Catalogue prices include VAT; tax is extracted, never added | accepted |
| [0029](0029-jsonschema-net-pinned-to-its-last-mit-release.md) | JsonSchema.Net is pinned to 8.0.5, its last MIT release | accepted |
| [0030](0030-protocol-routes-are-not-in-the-openapi-document.md) | A route whose protocol publishes its own discovery is not in the OpenAPI document | accepted |
| [0031](0031-model-weights-are-dependencies.md) | Model weights are dependencies, served from a pinned revision | accepted |
| [0032](0032-knowing-what-the-shop-does-not-sell.md) | Knowing that the shop does not sell something is a query-understanding decision, not a threshold on a retrieval score | accepted |
| [0033](0033-a-design-artefact-is-disposable.md) | A design artefact is disposable, and the design tool is an experiment with a removal condition | accepted |
| [0034](0034-a-ramps-middle-step-is-a-fill.md) | A ramp's middle step is a fill; only its dark step carries text | accepted |
| [0035](0035-the-architecture-is-drawn-from-models-held-to-the-code.md) | The architecture is drawn from models, and each model is held to the code | accepted |
| [0036](0036-observability-is-a-second-stack-behind-a-switch.md) | Observability is a second stack behind a switch, browser telemetry is a port, and every feature names the event that proves it | accepted |
| [0037](0037-traffic-is-simulated-from-a-declared-model.md) | Traffic is simulated from a declared model, seeded, and labelled as synthetic | accepted |
| [0038](0038-a-test-order-is-a-mode-the-credential-decides.md) | A test order is a mode the credential decides, fixed at placement | accepted |
| [0039](0039-ucp-checkout-is-a-session-an-agent-owns.md) | UCP's checkout is a session an agent owns, served on both transports, and `/mcp` is not anonymous for it | accepted |
| [0040](0040-a-mandate-is-verified-against-terms-the-shop-signed.md) | A checkout mandate is verified against terms the shop itself signed | accepted |
| [0041](0041-staff-usage-is-measured-under-a-notice-not-consent.md) | Staff usage is measured under a notice, not by consent | accepted |
| [0042](0042-work-is-done-beside-the-list.md) | Work is done beside the list; what can be undone offers Undo, what cannot asks first | proposed |
| [0043](0043-a-closed-set-is-declared-in-the-contract.md) | A closed set is declared in the contract, spelled as its C# name | accepted |
| [0044](0044-a-campaign-makes-pricing-a-writer.md) | A campaign makes Pricing a writer | accepted |
| [0045](0045-echarts-is-an-experiment-with-a-removal-condition.md) | ECharts is an experiment with a removal condition — decided: ECharts stays | accepted |
| [0046](0046-a-day-is-the-shops-day.md) | A day is the shop's day (NodaTime, the shop's time zone) | accepted |
| [0047](0047-folders-by-theme-and-a-signal-store-per-state.md) | Folders by theme, and a SignalStore for every state with a life of its own (NgRx SignalStore, tier 1) | accepted |
| [0048](0048-an-order-records-what-the-dashboard-reads-when-it-happens.md) | An order records what the dashboard reads when it happens — its channel, its confirmed and shipped moments, each line's category — and older orders are counted apart | accepted |
| [0049](0049-tailwind-utility-first.md) | Tailwind, utility-first, over a theme that is the design tokens | accepted |
| [0050](0050-the-tokens-are-tailwinds-theme.md) | The design tokens are Tailwind's theme; the plain file is generated | accepted |
| [0051](0051-angular-22-and-ngrx-22-as-their-guides-say.md) | Angular 22 and NgRx 22 as their guides say: defaults unwritten, resource extensions, rxResource over ports | accepted |

Format: context → decision → consequences. Keep each under a page.

The gaps are deliberate: `roadmap.md` reserves 0018-0023 for decisions later
phases are already committed to making, and renumbering them as they land would
break the references the roadmap and the code already carry. 0017 was the first
of those reservations to be cashed, in phase 7; 0020 and 0021 followed in phase
9, and 0023 in phase 8 — with two flags rather than the five its title
reserved, because a flag guards behaviour that exists, and none since 2026-09-17,
when one became a capability and the other met its removal condition; 0018 in phase 10,
written first, before the domain it constrains. 0022 was written in phase 11, once the
registry, delegation, mandates and the shop's own rules existed to be decided about.
What is still reserved and still unwritten: **0019** (the copilot proposes
commands, never prose).
