# 0003 — Ports + keyed adapters for connectors and payments

**Status**: accepted

## Context
N catalog sources (seed, Shopify, Medusa…) and 3 payment providers (fake,
stripe-mock, Stripe test) must be interchangeable, and the repo must run with
zero sign-ups.

## Decision
One port per boundary (`ICatalogSourceConnector`, `IPaymentProvider`);
adapters register as keyed services resolved by name from configuration or the
request. Defaults: `seed` and `fake`. Every port ships an abstract contract-test
suite that all adapters inherit.

## Consequences
New source/provider = one class + one DI line, zero conditionals in handlers.
An adapter without its contract-test subclass does not merge.
