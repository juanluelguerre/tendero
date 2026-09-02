# 0016 — Prices are quoted live and frozen at order time

**Status**: accepted

## Context

A price is not a fact about a product. It is the answer to a question that has
four inputs — what is in the cart, who is asking, which tariffs are in force, and
which promotions are running — and every one of them can change between the
moment a shopper sees a total and the moment they pay.

Two obvious designs both fail, in opposite directions.

**Store the computed price on the cart.** Then a tariff edit in the backoffice
silently stops applying to the carts that already exist, and the shop quietly
sells at yesterday's price for as long as those carts live. Worse, nothing can
tell the difference between a stale price and a correct one.

**Recompute at checkout and charge whatever comes out.** Then the total the
shopper agreed to is not the total they are charged. That is a support ticket at
best, and for an agent — which will have reasoned about the amount against a
mandate — it is a broken contract.

The same problem turns up twice more later, which is what makes it worth solving
once. Phase 5 needs `PlaceOrder` to know whether the quote it was handed still
holds. Phase 11 needs to check that an AP2 mandate still covers an amount. Both
are the same shape: something was frozen, and it has to still hold.

## Decision

**Pricing quotes live; `Order` freezes.** Between the two sits a `PriceQuote`
carrying an expiry and a **fingerprint of its inputs**.

The quote is recomputed on every cart change and never stored as truth. Checkout
revalidates it and gets one of three answers — `Valid`, `Expired`,
`InputsChanged` — and the last two are told to the shopper in different words,
which is why the check returns a reason rather than a boolean.

**The fingerprint covers two things, and the second is the one that gets
forgotten.** What the shopper asked for — lines, quantities, coupons, shipping —
and *the data they were answered with*: a hash of the price book plus a hash of
the promotion set. Without that second half, dropping a price in the backoffice
would leave live quotes promising the old one, and revalidation would have
nothing to notice.

Once an order exists, ADR 0002's snapshot rule takes over: the order stores
promotion **codes and labels as strings**, exactly as it already stores
`ProductName`. `Ordering` never references a `Promotion`.

## Consequences

- Quotes are cheap and disposable. A quote id is not an identity worth persisting
  yet; two quotes of the same cart are two quotes, each with its own expiry.
- **A quote lives 15 minutes.** That is the window in which the shop commits to a
  price. Committing for an hour against a catalogue that changes is promising
  what cannot be kept.
- The fingerprint is over the *whole* promotion set, not the promotions that
  applied. Coarser than necessary — editing an unrelated promotion invalidates
  live quotes — and correct, which at six products and two tariffs is the right
  side to be wrong on. Narrowing it needs the evaluation to record what it read,
  and that is work with no observable payoff yet.
- A promotion's **name** stays out of the fingerprint. Retranslating a label does
  not change what anyone pays, and a translation pass should not invalidate every
  quote in flight.
- The canonical string is built by hand with invariant formatting rather than
  through `ToString()`. A different decimal separator would produce a different
  fingerprint for identical data — and `InvariantGlobalization=true` makes that
  look safe right up until it is not.
- The hash is truncated to 32 hex characters. It is a change detector, not a
  security boundary: nobody gains anything by forging a match into a price they
  would then be charged.
- **Rounding is a named policy, applied once.** `Money.Percent` deliberately does
  not round, so a discount, then tax, then a proportional split round once at the
  end rather than three times on the way. Away from zero, because that is what
  most people mean by rounding and what several tax authorities require.
- The mismatch path is the same mechanism as *"this mandate authorises up to
  200 €"*. One piece, two features — recorded here so phase 11 does not build a
  second one.
