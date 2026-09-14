# UCP JSON Schemas, vendored

116 files, 244 KB, copied verbatim from the specification's own repository at a
**pinned commit**:

```
https://github.com/Universal-Commerce-Protocol/ucp
commit 3b114a1d74c702e404abada75b4ad7b3955a562d   (2026-09-08)
source/schemas/**
```

## Why they are committed rather than fetched

A test that downloads the thing it validates against is not a gate. It fails
when a network fails, passes when a proxy caches something stale, and — worse —
its verdict changes under you when somebody else edits a file. Validation
against a moving target is not validation.

Pinned instead: the profile Tendero serves is checked against the schemas as
they stood on a named day. Moving to a newer specification is then a deliberate
commit with a diff, in the same shape as
[`UcpSpecification.Version`](../../../src/Ucp/UcpSpecification.cs) being a
constant rather than "whatever the latest is".

## Why all of them

The `$ref`s are relative and they reach a long way — a profile pulls in
`ucp.json`, `service.json`, `capability.json`, `payment_handler.json` and, from
`common/types/`, everything from `reverse_domain_name.json` to `price.json`.
Every file carries an absolute `$id` under `https://ucp.dev/schemas/`, so
registering the whole set resolves every reference **offline**, with no network
and no resolver of ours.

## Licence

The UCP repository is Apache-2.0. These are unmodified copies used to test
against, not redistributed as part of any product.
