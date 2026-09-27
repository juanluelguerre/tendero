# ADR 0039 — UCP's checkout is a session an agent owns, served on both transports, and `/mcp` is not anonymous for it

Status: accepted · 2026-09-27 · Related: [ADR 0016](0016-quoted-live-frozen-at-order.md), [ADR 0020](0020-transports-over-the-dispatcher.md), [ADR 0025](0025-authorise-before-placing.md), [ADR 0030](0030-protocol-routes-are-not-in-the-openapi-document.md), [ADR 0038](0038-a-test-order-is-a-mode-the-credential-decides.md), [ADR 0022 (reserved)](README.md) · Analysis: [`ucp-checkout-and-ap2.md`](../analysis/ucp-checkout-and-ap2.md) §6

## Context

UCP's checkout (release 2026-08-25) is five operations over a session the
business keeps: create, get, update (a full replacement), complete, cancel. The
business remembers what the agent said between calls — buyer, destination, the
chosen option, discount codes — and answers a status the business derives:
`incomplete`, `ready_for_complete`, `requires_escalation`, `complete_in_progress`,
`completed`, `canceled`. Tendero had the parts — a cart, shipping rates, a quote
whose fingerprint checkout re-verifies (ADR 0016), authorise-then-place (ADR 0025)
— and none of them has a place for that state, and `Ucp` owns none (ADR 0020).

UCP declares capabilities per **service**, not per transport, and the profile
already binds `dev.ucp.shopping` over REST and MCP. Declaring checkout therefore
promises its five tools on `/mcp` too — the endpoint CLAUDE.md describes as
read-only and anonymous.

## Decision

**1 · A checkout session is an aggregate in Ordering, `CheckoutSession`.** It owns
a cart for its lines (so the line rules are the cart's), the buyer, the
destination, the chosen option, the codes, the fingerprint of the quote last shown,
its mode (ADR 0038) and its owner — the agent. **Only `Open → Completed | Canceled`
is stored.** `incomplete`, `ready_for_complete` and `requires_escalation` are derived
each time from what the session lacks and what the shop can settle, so they cannot
drift from the facts. Prices are never stored; completion re-quotes and compares
against the fingerprint shown, and a moved price charges nothing and shows the new
one. A completed session shows what its order froze.

**2 · Placing an order is shared behaviour**, `OrderPlacement` at Ordering's root —
the storefront's checkout and UCP's both call it, and **the caller commits**, so a
session is completed in the same transaction as the order it placed.

**3 · A session belongs to the agent that opened it.** Any other principal is told
it does not exist. UCP's routes require an agent's token (`TenderoPolicyNames.Agent`).

**4 · The shop settles only what it can.** One payment handler is declared,
`com.elguerre.tendero.fake`, with `environment: sandbox`, and a checkout offers it
to a test credential only. A live agent's checkout ends in `requires_escalation`
with a `continue_url`: the storefront takes over the session's cart (the id in the
link is the credential, as a guest's order id is), a person pays, and paying
completes the agent's checkout. `CheckoutOptions.AgentsPayLive` turns that off the
day a real provider takes agents' money.

**5 · Negotiation is real, and bounded.** Every call reads the `UCP-Agent` profile
and intersects it with the shop's declarations (UCP's algorithm, extensions pruned
with their parents); only the checkout family is echoed. Profiles are fetched only
from `Ucp:TrustedPlatformHosts`, never following a redirect — the server-side
request forgery UCP warns about is closed by construction. In Development the API
publishes its seeded agents' profiles, the dev-issuer pattern applied to UCP.

**6 · The checkout tools share `/mcp`, and are the one part of it that is not
anonymous.** The shop's own tools and UCP's catalogue stay read-only and open; the
five checkout tools refuse, tool by tool, any caller without an agent's token. The
trust model does not change — UCP is the surface with its own principal, whatever
transport carries it — only the sentence that described `/mcp` as a whole.

## Consequences

- Declared: checkout, fulfillment (one method, one destination, one group) and
  discount (extending checkout). Not yet: **order** (it commits to signed webhooks,
  which need the keys AP2 brings), **cart** over UCP, the AP2 mandate (built in P11-4, ADR 0040),
  pickup, several packages, `complete_in_progress`.
- A rejected discount code is a warning and does not say why: the engine has no
  coupon finder, and a rejection that named the rule would be one.
- `Idempotency-Key` protects completion (the order's key); a key reused with a
  different body is not yet detected (UCP asks for a 409) — written down.
- Responses carry no `tax` entry: prices include VAT (ADR 0028), so VAT is shown as
  lines inside the total that sum to it, and a platform adding up entries gets the
  right number.
