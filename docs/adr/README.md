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
| [0012](0012-publishing-and-reindexing.md) | Publishing is an explicit step; the index is rebuildable (amended 2026-10-23: a migration is published at once) | accepted |
| [0013](0013-culture-negotiation.md) | Culture: explicit parameter wins, `Accept-Language` is the default | accepted |
| [0014](0014-context-map.md) | Contexts share no entities; the count is not the rule | accepted |
| [0015](0015-variant-is-the-indexed-unit.md) | The variant is the indexed unit; the product is the returned unit | accepted |
| [0016](0016-quoted-live-frozen-at-order.md) | Prices are quoted live and frozen at order time | accepted |
| [0017](0017-the-identity-provider-is-a-port.md) | The identity provider is a port; the first adapter was a development issuer | accepted |
| [0018](0018-the-ai-writes-claims-not-products.md) | The AI writes claims, not products | accepted |
| [0019](0019-the-copilot-proposes-commands-never-prose.md) | The copilot proposes commands, never prose: what a model produces that can change the shop is a registered command, stored unapplied for a person to decide | accepted |
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
| [0052](0052-assist-is-a-context.md) | Assist is a context: a proposal has a life of its own, and runs other contexts' commands only through `IProposableCommand` | accepted |
| [0053](0053-one-model-port.md) | One model port: profiles in configuration, one pipeline, one row per call in `telemetry.ai_invocations` | accepted |
| [0054](0054-read-tools-belong-to-their-contexts.md) | Read tools belong to their contexts: a context decides what a model may read of it, through `IReadTool` and a DTO with no customer in it — amended in phase 12b: a read has an audience | accepted |
| [0055](0055-an-answer-is-a-list-of-cited-facts.md) | An answer to a shopper is a list of cited facts, never prose of a model's own: a schema built from the turn's reads, grounding in code before anything is shown, the shop's card and not the model's | accepted |
| [0056](0056-a-category-declares-its-attributes.md) | A category declares its products' attributes: inherited down the branch, offered to whoever reads a source for claims and never enforced on a product; what a picture can show is the attribute's own to say. **Amended the same day**: a declared attribute is expected or *required*; a product without a required one is incomplete — shown, never refused | accepted |
| [0057](0057-a-picture-says-what-it-shows.md) | A picture says which of the product's axis values it shows; a variant has no picture of its own and its gallery is derived; a level between product and variant is deferred, with what would bring it back | accepted |
| [0058](0058-pictures-are-a-vector-space-of-their-own.md) | Pictures are a vector space of their own: SigLIP 2 in process, one point a picture keyed on the hash of its bytes, asked at endpoints beside the search and never inside it; a shopper's picture is never kept; the floors and the leg are left open with what was measured | accepted |
| [0059](0059-the-wire-has-two-readers.md) | The wire has two readers: money is a whole number of minor units with its currency, a refusal is an RFC 9457 problem with a code declared for its operation, a message is a code and a sentence; the contract names its version and who may call each operation. A refusal is data in the kernel, written and declared by the host, so the kernel stays free of HTTP | accepted |
| [0060](0060-a-recommendation-is-solved-not-generated.md) | A recommendation is solved, not generated: parse, retrieve, solve, explain, with a model at the two ends only; a pure solver held by properties, coverage before relevance, exact under a ceiling it declares; the trace is the explanation's only source; the same ask gives the same kit | accepted |
| [0061](0061-speech-is-a-port-of-ai.md) | Speech is a port of AI: `ISpeechToText` beside the chat profiles, AI's first slice for the transcription, each engine a keyed adapter and the model its options; a fixed 16 kHz mono WAV refused by its bytes; no engine, no microphone | accepted |
| [0062](0062-objects-in-a-photograph-are-found-by-a-detector.md) | The objects in a photograph are found by a detector, and the detector is OWLv2: `IObjectDetector` in `Search/Images`, measured against SigLIP's grid and the local Qwen before it was chosen; our export, tier 1, with the text tower and CLIP's tokenizer ours so the kinds stay configuration; no export, the photograph is one object | accepted |
| [0063](0063-a-picture-is-a-question-not-a-fact.md) | A picture is the shopper's question, never a fact: reads made by code over closed lists, each labelled as what it is (*similar in the shop*, never *this is*); nothing a model reads in a shopper's picture is written anywhere; a model that writes words never receives it; the picture is not kept | accepted |
| [0064](0064-a-saved-card-is-the-providers-token.md) | A saved card is the provider's token and what may be shown of it, never the card: the port gains save and forget, charging a kept card is the authorisation unchanged, the wallet is Ordering's, a token outlives nothing, and it never leaves the server | accepted |
| [0065](0065-the-sign-in-page-is-keycloaks-theme-in-the-shops-tokens.md) | The sign-in page is Keycloak's own `keycloak.v2` theme in the shop's generated tokens, with no template copied; the realm speaks es and en and the apps ask for theirs with `ui_locales`; it is platform, documented in `keycloak/README.md`, not a feature | accepted |
| [0066](0066-imported-history-is-born-closed.md) | Imported history is born closed: a transition table declares where its aggregate may be born, and `Order.Imported` and `ReturnRequest.Imported` are the named entries for orders and refunds that already happened — final state, no event, the source and its dates kept, idempotent by source | accepted |

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
0019 was the last reservation, written at phase 12's close, once Steward had been
built and measured. Nothing is reserved now.
