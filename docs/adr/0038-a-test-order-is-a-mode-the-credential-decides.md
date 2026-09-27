# ADR 0038 — A test order is a mode the credential decides, fixed at placement

Status: accepted · 2026-09-27 · Related: [ADR 0024](0024-the-outbox-is-the-process-manager.md), [ADR 0025](0025-authorise-before-placing.md), [ADR 0037](0037-traffic-is-simulated-from-a-declared-model.md), [ADR 0022](0022-an-agent-is-a-principal-not-a-customer.md) · Analysis: [`ucp-checkout-and-ap2.md`](../analysis/ucp-checkout-and-ap2.md) §2

## Context

An agent that drives a checkout needs to rehearse it: every test worth running
wants a real order, and none of them can want a real charge. A reader who had
built one said so, and said it had to exist *before* the protocol surface. Tendero
chose its payment provider **per process** (`CheckoutOptions.PaymentProvider`), so
the day the shop takes real money an agent has no way to try a checkout without
paying.

Neither protocol has the answer ready. **UCP** has no order-level test mode: it
has an `environment` (`sandbox` / `production`) on each payment handler's
configuration, declared by the business. **AP2 v0.2** has none; its *modes* say who
approved, not whether money moves. The same distinction everywhere — whether money
moves is a property of **how the order is paid**, not a flag the buyer sends.

## Decision

**1 · An order has a mode, `Live` or `Test`, fixed when it is placed** and never
changed, like the price it froze (ADR 0016). A test order goes through the whole
saga — reservation, state machine, audit, events — exactly as a live one does.

**2 · The server decides it, from the credential.** A test credential carries
`tendero_mode: test`, signed by the identity provider like `agent_id`; the API reads
it into `CommercePrincipal.IsTest`, and `OrderModes.Of(principal)` is the one rule.
Nothing in a request body, a header or a query string can make an order a test —
a mode an agent could choose is a way to receive goods without paying, or to be
charged by mistake. The development issuer seeds `claude-desktop-test`; Keycloak
will carry it as a client attribute.

**3 · A test order pays through the test provider**, whatever the shop takes money
with: `CheckoutOptions.TestPaymentProvider` (the fake) against `PaymentProvider`,
chosen in one place, `ProviderFor(mode)`.

**4 · A test order is never shipped, and ends by itself.** `Order.Ship` refuses it —
a rule about which orders are real, not an edge missing from the transition table.
When it reaches `Confirmed`, the worker cancels it with the stop code
`test_order_ended`, and the compensation every cancellation already runs gives its
stock back and voids its hold. Nothing new to keep in step; the rehearsal exercises
the path a live order rarely takes. A timer that ended it later was not taken: it
needs a scheduler the system does not have, and a rehearsal holding stock is stock
a real shopper cannot buy.

**5 · Every order metric carries `order.mode`**, and the *Shop pulse* leaves `test`
out. ADR 0037's `X-Tendero-Synthetic` stays a label that changes no behaviour: a
simulated agent that should not move money uses a test credential.

**6 · On UCP, a test order is an order paid through a `sandbox` handler.** When the
checkout capability is declared (`P11-1`), the fake provider is a handler whose
`environment` is `sandbox`, offered to test credentials only. Nothing is added to
UCP's objects: the mode is Tendero's domain, the environment is UCP's wire.

## Consequences

- The order id the payment is authorised under is now the order's own id: checkout
  minted one for the provider and `Order.Place` minted another, so the provider's
  record and the order named different orders. Found writing this.
- A rehearsal is visible where people work: the backoffice marks it *Prueba / Test*
  beside its status, and its stop reason reads *test order ended* in both apps.
- Returns, refunds and delivery never see a test order, since it is never delivered.
  An agent that wants to rehearse a return needs a different mechanism, not this one.
- The credential becomes part of onboarding an agent (`P11-3`): an agent is given a
  test credential first, and a live one when its rehearsals end cleanly.
