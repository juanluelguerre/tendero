# ADR 0022 — An agent is a principal, not a customer

Status: accepted · 2026-09-27 (reserved in the roadmap since 2026-09-02, written once phase 11 had built what it decides) · Related: [ADR 0017](0017-the-identity-provider-is-a-port.md), [ADR 0027](0027-audit-writes-outside-the-transaction.md), [ADR 0038](0038-a-test-order-is-a-mode-the-credential-decides.md), [ADR 0039](0039-ucp-checkout-is-a-session-an-agent-owns.md), [ADR 0040](0040-a-mandate-is-verified-against-terms-the-shop-signed.md) · Analysis: [`ucp-checkout-and-ap2.md`](../analysis/ucp-checkout-and-ap2.md) §5, §7, §8

## Context

A shop that sells to agents has to answer questions a shop that sells only to people
never asks. Who placed this order: the person, or software acting for them? Which
software, run by whom, and does the shop trust it? What did the person allow it to
spend? Which of the shop's own limits apply, and to whom?

The easy model is to treat an agent as one more customer: an account with a login
that happens to be a program. It fails on the first question. Once the agent *is*
the customer, the order no longer says a person was involved, the audit row cannot
say on whose behalf, and a rule for agents cannot tell who it applies to. It also
fails the other way round: an agent is not a buyer, and giving it an account with
an address book and an order history models something that does not exist.

## Decision

**1 · Two identities on one principal.** `CommercePrincipal` carries a `Customer`
(the person, when there is one) and an `Agent` (the software, when there is one),
side by side. A person shopping has only the first, an agent's own token only the
second, and an agent acting for a person both. The identity provider is a port
(ADR 0017), so every token issuer gets the same treatment.

**2 · An agent acts for a person visibly, never as them.** Delegation is RFC 8693
token exchange: `sub` is the person and `act.sub` is the agent (P11-3). The API
reads the standard `act` claim as well as our own `agent_id`, so moving to
Keycloak's delegation changes nothing downstream. *Impersonation*, where the agent
becomes the person, is not offered, because a shop that cannot tell whether a
person or their agent bought something cannot answer anything afterwards.

**3 · The registry says what the shop thinks, not who the agent is.** The token says
who. The `Agent` aggregate in Accounts says what the shop thinks of it: its
operator, a trust level (`Known`, `Verified`) and whether it is suspended, with a
reason (P11-2). The principal carries that as a value, `AgentStanding`. An agent
nobody registered is `Untrusted` and shops under tighter rules, rather than being
refused or silently turned into a row by being seen. The registry holds no keys and
no secrets: credentials belong to the identity provider, and the key that binds a
person's approval travels in the mandate (ADR 0040).

**4 · Three layers of limits, three owners.** UCP carries no limits. AP2's mandate
carries the person's, and the shop verifies it and freezes its scope onto the order
(ADR 0040, P11-5). The shop's own rules, which no protocol defines, apply on top and
are keyed to the agent: suspended agents place nothing, a cap per order by trust,
and a ceiling on orders per hour (P11-6). They run where the facts they need exist,
which is at the till, on the final total, inside the transaction that places the
order.

**5 · Every trace says both.** The audit row names the customer and the agent
(ADR 0027), and a denial names the rule that refused. The order keeps the agent that
placed it (`PlacedBy`) and the mandate it was placed under, and checkout metrics
carry `agent.trust`. "Who acted, on whose behalf, under what authority" is a query,
not an investigation.

## Consequences

- The dashboard can say what share of sales agents bring and what they were refused
  (P11-7) without joining logs, because the order and the audit already say it.
- A rehearsal is decided by the agent's credential (ADR 0038), not by the request:
  the same principal model answers "is this real money?".
- Not built: agent keys in the registry for signature-based identification (Web Bot
  Auth, HTTP Message Signatures). A verified agent is one the shopkeeper vouched
  for, not one whose requests are signed. When signed requests arrive, the registry
  gains the key reference the roadmap drew, and `Verified` gains a cryptographic
  meaning.
- A suspended agent can still open a checkout session. It is refused where it would
  spend, which is enough to protect the money and less than the whole story.
