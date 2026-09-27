# UCP checkout, AP2 mandates and Know Your Agent — what the specifications say, and what Tendero decides

Phase 11's research, written as it happens (the rule since phase 10: a decision is
recorded when it is taken, with its reason). The plan for the phase was written in
2026-09 from memory of the specifications; this document reads them again at a
pinned revision before any code is written, and says where the plan was wrong.

## 1 · What was read, and at which revision

| source | revision | licence | read on |
|---|---|---|---|
| UCP specification (`Universal-Commerce-Protocol/ucp`) | `main` at `1b4e7bb`, 2026-09-25; the vendored schemas in `tests/Ucp.Tests/ucp-schemas` stay at their own pin | Apache-2.0 | 2026-09-27 |
| AP2, *Agent Payments Protocol* (`google-agentic-commerce/AP2`) | tag **`v0.2.0`**, 2026-04-28 (commit `b4587ac`); `main` at `e1ea56d`, 2026-04-29 | Apache-2.0 | 2026-09-27 |

Both are Apache-2.0: vendoring their JSON Schemas for contract tests is allowed, as
it already is for UCP's.

## 2 · Is there a test mode to use rather than invent? (`P11-0`)

The reader's reply that created `P11-0` asked for a no-charge order mode *before* the
protocol surface, and the plan said: read UCP and AP2 first, and use theirs if it
exists.

**UCP has no order-level or checkout-level test mode.** What it has is an
`environment` on each **payment handler**'s configuration — `"sandbox"` or
`"production"`, default `"production"` (`docs/specification/payment/guide.md`, the
handler's `business_config`, `platform_config` and `response_config`; the Google Pay
handler's own `"TEST"` in the overview's example). It is declared by the **business**
in its profile, per handler instance: the protocol's position is that *whether money
moves* is a property of **how the order is paid**, not a flag the buyer sends.

**AP2 v0.2 has no test mode either.** Its only *modes* are *Human Present* and
*Human Not Present*, which say who approved, not whether it is real. The repository's
"simulation" is in its Python samples (x402), not in the protocol.

**Decision (2026-09-27): Tendero's `OrderMode` is its own, and UCP already has the
place to express it.** An order is `Live` or `Test`, fixed at placement, decided by
the server from the credential — never from the request body. On the UCP surface a
test order is exactly *an order paid through a handler declared with `environment:
"sandbox"`*: the shop declares its fake payment provider as a sandbox handler, and
only an agent whose credential is a test credential is offered it. Nothing is added
to UCP's objects; the mode is Tendero's domain, the environment is UCP's wire.

## 3 · AP2 v0.2 is not the AP2 the plan describes

The plan (and the roadmap it came from) speaks of AP2's **Intent, Cart and Payment
mandates** as W3C Verifiable Credentials — AP2 **v0.1** (2025-09-16). **v0.2**
(2026-04-28, contributed to the FIDO Alliance the same month) replaced them:

- **Two mandate types**: a **Checkout Mandate** (proof to the *merchant* that the
  agent may complete this checkout) and a **Payment Mandate** (proof to the
  credential provider, network and *merchant payment processor* that it may pay).
- **Open and closed.** An *open* mandate is signed by the user on a trusted surface
  and carries **constraints** and the agent's key (`cnf`); a *closed* mandate is
  bound to one transaction and, when the human is not present, signed by the
  **agent's key**. Verifiers always receive closed mandates, plus the open ones they
  descend from when the agent signed.
- **SD-JWT, versioned by `vct`**: `mandate.checkout.open.1`, `mandate.checkout.1`,
  `mandate.payment.open.1`, `mandate.payment.1`. "Implementations MUST match the
  exact `vct` string" — which is precisely the plan's *verifier keyed by format
  version* (`P11-4`), now with the key named by the specification.
- **The merchant signs the checkout.** The merchant returns a **merchant-signed
  Checkout JWT**; the closed Checkout Mandate binds to it by `checkout_hash`. With
  UCP, "this MUST be the Checkout object". The merchant answers with a **Checkout
  Receipt**.
- **Constraints the merchant evaluates**: `checkout.allowed_merchants`,
  `checkout.line_items`. **Constraints the payment side evaluates**:
  `payment.amount_range`, `payment.budget` (with `payment.agent_recurrence`),
  `payment.allowed_payees`, `payment.allowed_payment_instruments`,
  `payment.reference`, `payment.execution_date`.

**What it changes in phase 11.** The demo — *"the mandate authorises up to 200 €"* —
is a `payment.amount_range` (or `payment.budget`) in an **open Payment Mandate**,
evaluated where the payment is made: in Tendero, behind the payment port. The
merchant's own checks are the checkout constraints and the binding to the Checkout
JWT it signed. `P11-4` becomes: sign checkouts, verify closed mandates against
their open parents by `vct`, evaluate constraints, answer with receipts. The
decisions for `P11-2` … `P11-6` are taken below as each task starts.

## 4 · Test orders, as built (`P11-0`, ADR 0038)

- `OrderMode { Live, Test }` on the order, set by `Order.Place` and never again.
- The rule, in one place: `OrderModes.Of(CommercePrincipal)` — a principal whose
  token carries `tendero_mode: test` places test orders. The development issuer
  seeds `claude-desktop-test`.
- Payment: `CheckoutOptions.ProviderFor(mode)` — the configured provider for live,
  `TestPaymentProvider` (the fake) for test.
- The end of a rehearsal: `Order.Ship` refuses a test order;
  `EndTestOrderWhenConfirmed` cancels it with `test_order_ended` once confirmed,
  and `ReleaseStockOnOrderCancelled` / `VoidPaymentOnOrderCancelled` do the rest.
- Seen: `order.mode` on `tendero.checkout.attempts`, `tendero.orders.test_ended`,
  the *Shop pulse*'s seven queries filtering `order_mode!="test"`, a *Prueba* badge.
- **Alternatives weighed.** A request flag (`"test": true` in the checkout body):
  refused — the buyer would decide whether it pays. A separate test process or
  database: refused — a rehearsal that does not run on the shop's own saga proves
  nothing about it, which was the reader's whole point. A timer that ends the
  rehearsal later: refused — no scheduler, and stock held for nobody.

## 5 · The agent registry (`P11-2`)

**Decision (2026-09-27): the aggregate is `Agent`, not `AgentPrincipal`.** The
roadmap's name mixed two things. The *principal* is the runtime identity a token
proves — the identity provider's statement, which Tendero only validates
(invariant 10). The *registry entry* is what the shop thinks of that identity:
who operates it, how far it is trusted, whether it is suspended. The second is a
fact the shop owns, like a customer's tariff, and it lives in Accounts; the
principal carries it as a value (`AgentStanding(Trust, Suspended)`), resolved on
the key once per request like the segment.

- **Untrusted is not a registry state.** An agent with a valid token that nobody
  registered is `AgentStanding.Unknown`; registering makes it at least `Known`, and
  it never goes back — that would be unregistering, and an audit trail is not
  unregistered. Suspension is the switch, with its reason.
- **No keys, no secrets.** The roadmap sketched `PublicKeyJwkRef` on the agent.
  AP2 v0.2 makes it unnecessary for mandates: the agent's key is in the open
  mandate's `cnf`, signed by the *user*, so the shop never has to be told it. UCP's
  request signing (the platform's key in its profile) is a separate question for
  `P11-3`, and it too belongs to the platform's published profile, not a copy here.
- **What it enables.** `agent.trust` on the checkout metric answers *who buys
  through agents the shop does not know*; `P11-6`'s rules read `Standing` — a
  suspended agent is refused, an unknown one limited.
