# ADR 0024 — The outbox is the process manager, and it is orchestrated from Ordering

Status: accepted · 2026-09-03 · Supersedes nothing · Amends [ADR 0007](0007-search-reads-catalog-domain.md)

## Context

Phase 4 adds `Inventory` and the first real cross-context process in the system:
an order is placed, stock has to be held, and if it cannot be held the order has
to cancel itself. Three things had to be decided, and only the first one looked
like a decision.

**1 · Whether to add a saga mechanism.** The vocabulary invites one — process
manager, correlation id, saga state, compensation. Every framework that offers
them (MassTransit, NServiceBus, Dapr Workflow) is either licensed in a way
[the dependency policy](../../CLAUDE.md) forbids or a runtime dependency far
larger than the problem.

**2 · Which context holds the process.** [`roadmap.md`](../analysis/roadmap.md)
§A5 drew the arrows as `Inventory on OrderPlaced → StockReserved | StockRejected`
and, four lines later, required an architecture rule that `Inventory.*` must not
depend on `Ordering.Domain`. Both cannot be true: a handler for `OrderPlaced` in
Inventory is a reference to an Ordering type.

**3 · What holding stock means for the order.** The same section drew
`on StockReserved → order.Confirm()`.

## Decision

**No saga mechanism is added. The outbox already is one**, and each arm of the
process is an ordinary `IDomainEventHandler<T>`:

```
Ordering   Order.Place()      → OrderPlaced
Ordering   on OrderPlaced     → IStockLedger.ReserveAsync(...) → held | refused
                                refused ⇒ order.Cancel(reason) → OrderCancelled
Ordering   on OrderCancelled  → IStockLedger.ReleaseAsync(...)      (compensation)
Ordering   on OrderShipped    → IStockLedger.CommitAsync(...)
```

There is no saga state table, no correlation store and no timeout service. The
correlation id is the `OrderId` that travels on every event; the saga's state is
the order's status and the reservation's status, each already a declarative
transition table; the durability is the outbox's, which was written in phase 1.

**The process is orchestrated from `Ordering`, not choreographed.** All three
handlers live in `Ordering/Features/StockSaga`, and they reach Inventory through
`IStockLedger` — a port whose whole surface takes and returns **values**: SKUs,
quantities, an `OrderId`, a refusal sentence. `Inventory` therefore references
SharedKernel and nothing else, exactly like `Pricing`, and two architecture
rules compute that by reflection.

The direction is chosen rather than inherited. Ordering already knows what an
order is and what its lines are; teaching Inventory that would make the
allocation strategies untestable in isolation and would give the reservation two
reasons to change. The cost is that `Ordering` knows Inventory's *ports*, which
is one dependency in one direction and the one the invariant permits.

**Holding stock does not confirm the order.** `Order.AllowedTransitions` routes
`Pending → PaymentAuthorized → Confirmed`, and stock says nothing about whether
anybody paid. The roadmap's arrow was drawn before the transition table was read.
The saga holds; payment confirms. A test pins it by name.

**Search may project Inventory.** `StockLevelChanged → outbox → inStock` on the
search document is a second projection edge, which is the widening ADR 0007
anticipated: *Search may reference any context it projects; no context may
reference Search.*

## Consequences

**What this buys.**

- Every arm is testable by calling it. There is no harness in `StockSagaTests`, because there is nothing to arrange but a fake ledger and a fake repository.
- Compensation is not a special path. `ReleaseStockOnOrderCancelled` listens to `OrderCancelled` and not to the reservation failing, so it runs for a declined card, a shopkeeper cancelling by hand, or the saga's own refusal — three causes, one handler.
- The failure mode is visible. A refusal writes a `Reservation` row in `Released` carrying its reason, so "the order cancelled itself" is a sentence somebody can read on a screen rather than an absence in a log.

**What it costs, stated plainly.**

- **At-least-once delivery is the handler's problem.** Every arm is idempotent by looking at state it does not own: reserving checks the order is still `Pending` and relies on a unique index on `reservations.order_id`; committing and releasing are no-ops on a reservation that is not `Held`. An integration test redelivers `OrderPlaced` and asserts the stock is held once.
- **Compensation takes a second drain.** The cancellation the saga itself causes raises `OrderCancelled`, which is a new outbox row picked up on the next pass. That is correct and it is slower than a call chain, and a test that drained once would have looked green while the release never ran.
- **The outbox marks a message processed whether or not anybody handled it.** An assembly the worker forgets to scan is silent: orders are placed, messages drain, no stock is ever held. This is not hypothetical — the saga shipped with the worker scanning only `Search`, and it was caught by naming each handler in `WorkerContainerTests` rather than by anything the build or the domain tests could see.
- **No timeouts.** `Reservation.Lifetime` is 15 minutes and `HasExpiredAt` exists, but nothing sweeps expired holds yet. That needs a scheduled worker, and it is deferred until carts exist (phase 5), where an abandoned cart is the thing that actually produces stale holds.

**Where this gets reused.** Phase 5's payment flow is the same shape with a
different port, and the shipping/return loop adds two more arms to the same
table. If a third context ever needs to participate, the question to re-ask is
whether the orchestrator still belongs in `Ordering` — not whether to introduce
a framework.
