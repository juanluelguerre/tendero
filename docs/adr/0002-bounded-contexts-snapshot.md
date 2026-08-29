# 0002 — Two bounded contexts; orders snapshot products

**Status**: accepted

## Context
Products change (price, translation, archive); historical orders must not.

## Decision
`Tendero.Catalog` and `Tendero.Ordering` share no entities. `OrderLine` stores
a snapshot: product id, name resolved in the buyer's culture, frozen unit
price. `Order.Culture` records the buyer's language at purchase time.

## Consequences
Retranslating or repricing the catalog never corrupts history. Cross-context
needs are served by events, not joins.
