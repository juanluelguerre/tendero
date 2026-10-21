# ADR 0064 — A saved card is the provider's token, kept by Ordering, and never the card

**Status**: accepted (2026-10-21, phase 15, `P15-4`)

## Context

Phase 15 lets a signed-in shopper keep a way to pay (`docs/specs/shopper-account.md`,
decision 5). A shop that keeps cards is in the business of holding payment data,
and the cheapest mistakes are permanent: a card number written to a log, a token
that outlives the customer's wish to keep it, an Accounts that holds credentials
for a port it does not own. `IPaymentProvider` already treated `Instrument` as an
opaque token, the one field never logged — by convention, with no attribute or
test behind it.

## Decision

**What is kept is what the provider hands back, and nothing else**: its token, and
what may be shown — the brand, the last four digits, the month and year it
expires (`SavedInstrument`, `SavedMethod`). Never a card number, never a CVC,
never a full expiry date. The number is typed into the provider's own field (an
iframe in a real adapter; the fake's named test cards here) and never reaches
Tendero, not even in transit.

**The port gains two operations that touch no money**: `SaveInstrumentAsync`
(a one-time instrument in, a token out) and `ForgetInstrumentAsync` (idempotent;
a token never minted is as forgotten as one deleted). **Charging a kept card is
`AuthorizeAsync`, unchanged**, with the token as the instrument. The contract
suite holds every adapter to: save → charge → forget → charge again is declined;
an expired card is declined with a reason a shopper reads; one card kept twice
is two tokens, and forgetting one keeps the other.

**The wallet is an aggregate of `Ordering`**, keyed by the customer, because
`Ordering` owns the payment port: an `Accounts` holding payment tokens would
have to reference it, which is the wrong direction (invariant 1). One default,
five at most; a default removed leaves none.

**A token outlives nothing.** Removing a method asks the provider to forget it
first. When the provider cannot be reached, the method leaves the wallet at once
— never offered again, not at checkout either — and its token waits in the
wallet's `ToForget` list for `ForgetSavedCardsWorker`. Erasing the account
(`P15-9`) forgets every token the same way.

**The token never leaves the server.** The wire carries what may be shown and an
id the shop minted (`SavedMethodView`); checkout charges a kept card by that id
(`PlaceOrderRequest.savedMethodId`), and the handler reads the token from the
caller's own wallet, through the provider that kept it, for a person and never
an agent (decision 8). An id from somebody else's wallet is declined, with
nothing written.

**Held by attributes and tests, not by convention**: `[AuditRedacted]` on the
one-time instrument (`KeepCardCommand.Instrument`, `PlaceOrderCommand.Instrument`);
spans carry counts and outcomes, never a token; an architecture rule fails on any
member named like a card number, a CVC or a full expiry date anywhere in the
solution — a guard against the mistake, not proof of its absence.

## Consequences

- **The token is stored as the provider returned it**, in `ordering.wallets`
  (jsonb). A provider's token charges nothing without the shop's own credentials
  with that provider, which never live in this database; encrypting it at rest is
  deferred until a real adapter exists, with that as the reason to revisit.
- **The fake keeps one piece of state**: the tokens forgotten since the process
  started. The wallet deletes its row in the same change, so a forgotten token is
  never offered again after a restart either.
- **Agents use none of it in phase 15.** A mandate naming a saved method needs a
  grant per agent and per method and a place to see and withdraw it; the spec
  writes that design down and schedules it.
- **Gift vouchers and split payments stay out**: a second tender is a liability
  the shop tracks, not a card, and the port's own comment refuses partial
  payments.
