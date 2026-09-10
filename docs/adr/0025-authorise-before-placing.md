# ADR 0025 — Checkout authorises the payment before it places the order

Status: accepted · 2026-09-03 · Extends [ADR 0016](0016-quoted-live-frozen-at-order.md) and [ADR 0024](0024-the-outbox-is-the-process-manager.md)

## Context

Checkout has to do five things and they cannot all be reversed: revalidate the
quote, price the delivery, take the money, create the order, close the cart.
Two of them are irreversible from our side — a hold on somebody's card, and an
order that raises an event the whole saga reacts to — so the question is which
one goes first.

**Place, then authorise** is what the state machine reads like: `Order.Place`
raises `OrderPlaced`, the saga holds stock, and the payment result then moves the
order to `PaymentAuthorized` or `PaymentFailed`. It is the version phase 4 was
written for.

**Authorise, then place** is what most card-not-present checkouts do, and it
means a decline never creates anything.

Both leave a mess in the mirror case. The first leaves an order and a held
reservation behind every declined card; the second leaves a hold on a card for an
order that turns out to have no stock.

## Decision

**Authorise first. Place second. Both in one transaction with the cart's
closure.**

The deciding argument is what each failure costs and what can clean it up.

A **declined card** is the common case — it happens to real shoppers several
times a day — and after this decision it costs nothing at all: no order row, no
reservation, an untouched cart, and a sentence the shopper can act on. The
alternative would have left a `PaymentFailed` order holding stock, and **nothing
sweeps those**: `Reservation.Lifetime` is fifteen minutes and `HasExpiredAt`
exists, but no worker reads them (the standing backlog says so). A failure mode
with no cleaner is a failure mode that accumulates.

**No stock after authorising** is the rare case, and it is the one the system can
actually clean up. `IPaymentProvider` grew a fourth operation for it:

```
OrderPlaced      → reserve → refused → order.Cancel(reason)
OrderCancelled   → IStockLedger.Release        (nothing held; free)
OrderCancelled   → IPaymentProvider.Void       (the hold goes back)
```

`VoidAsync` is not `RefundAsync`, and the port refuses to let them be confused:
a refund takes a **capture** reference, a void takes an **authorisation**, and
crediting money that was never taken is a different conversation with the
customer and with the bank. Without the fourth operation the honest answer would
have been "the hold expires in about a week", which is what a shop does when it
has no way to say so.

Two consequences fall out and are worth stating.

**`ReserveStockOnOrderPlaced` accepts `PaymentAuthorized` as well as `Pending`.**
Both facts are written in one transaction, so by the time the outbox drains the
order is normally already authorised. A guard that only accepted `Pending` would
have meant no ordinary order ever held stock — and it very nearly shipped.

**Confirmation hangs off the payment, not off the stock.** `ConfirmOrderWhenPaidAndHeld`
listens to `OrderPaymentAuthorized` and asks the ledger whether the hold exists,
because the payment event is the later of the two in every ordinary order. ADR
0024 already recorded that the roadmap's `on StockReserved → Confirm()` was wrong
against `AllowedTransitions`; this is the arrow that replaces it.

## Consequences

- **The demo is a refusal.** Picking "a card the bank declines" leaves the basket exactly as it was, which is the behaviour a shopper expects and the one that is easiest to get wrong.
- **Capture happens on shipping**, not on confirming, for the same reason stock is committed there: confirming is a promise and shipping is a fact. A shipped order whose payment still reads `Authorised` is therefore a real report — the backoffice shows the two states apart on purpose.
- **A capture that fails after shipping is logged and left uncaptured**, deliberately. It is a human problem — goods gone, money not moved — and dead-lettering the message would hide it behind a retry count. The `card-capture-fails` instrument exists so that path can be reached in a test at all.
- **The idempotency key is checked before anything is charged**, and it is the same key handed to the provider. Pressing pay twice, a proxy retrying a timeout and an agent reissuing a request are one order and one hold.
- **What this does not solve**: an authorisation whose void also fails. It is logged loudly and left to expire, which is the real-world behaviour and is now a decision rather than an omission.
- **Amended 2026-09-10 — a failure webhook cancels.** One consequence of this decision went unnoticed for a week: no saved order is ever `Pending`, and `Pending` is the only state the table lets reach `PaymentFailed`. The payment webhook still tried to fail authorised orders, so every real `payment.failed` threw, answered 500 and was retried by the provider forever. It now **cancels** an order whose money has not moved (`PaymentAuthorized` or `Confirmed`, no capture) under the `payment_declined` code, which puts it on the compensation path above — stock released, hold voided — with no new edge in the table. The other three cases leave the order untouched, and they are not the same: **after a capture** it is logged as a warning, since money that really went back is a refund or a chargeback somebody has to chase; **on an order that shipped uncaptured** it is logged as a warning too, because the goods left and the money did not — it cannot be cancelled, and a return is the customer's to start and has no capture to refund, so it needs a person, who will usually ask for the payment again before asking for the goods back; **on an order already cancelled** it is dropped quietly, because its stock and hold went back when it was cancelled. `PaymentFailed` is left reachable only from `Pending`, which is where a declined card would put an order if checkout ever placed before authorising again.
