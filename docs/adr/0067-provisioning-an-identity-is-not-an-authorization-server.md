# ADR 0067 — Provisioning an identity is not writing an authorization server

**Status**: accepted (2026-10-24, phase 16, S2b, `P16-17`; spec `docs/specs/shopify-leave.md`, decision 3 and S2b-1, A)

## Context

Invariant 10: the identity provider is a port, the shop only ever validates tokens,
and an authorization server is never a feature. Keycloak signs people in; the API
reads the token it signed.

A merchant leaving Shopify brings their customers, and a customer needs a way to
sign in to see the orders they placed there. No platform exports a password. The
realm allows no self-registration (`registrationAllowed: false`): accounts are made
by the shop, not by the shopper. So an imported customer with no identity would be
somebody with an order history and no door to it. The alternative of opening
registration to everyone, and linking by a verified email at first sign-in, is a
product decision larger than a migration (spec, S2b-1, B).

## Decision

**The shop may ask the provider to make an identity, through a port**
(`IIdentityProvisioner`), for somebody it already knows: an imported customer, with
their email and name and **no password**. It never sets a password, never issues or
signs a token, never sees a credential, and never decides who is signed in. On their
first visit the person uses the provider's own *forgot password*: the provider
emails them a link, which both sets the password and proves the address is theirs.

- **One adapter, Keycloak's admin API**, through a service account that may manage
  users and nothing else, held by a contract suite that an in-memory fake passes
  too. **The development issuer gets none, on purpose**: its own retirement
  condition is "the day it grows a user registry, Keycloak should have arrived",
  and provisioning into it would be exactly that. With the development issuer, an
  import brings customers as guests, and says so.
- **Idempotent by email**: provisioning somebody who already has an identity answers
  with that one. That answer is also how the import recognises a person the shop
  already had, and links rather than duplicates them.
- **Without a provisioner** (none registered, or it refuses), the customer comes as a
  guest with an email, and the import says so. Their orders still have an owner.
- **No email is sent at migration** (S2b-3). Keycloak sends the reset email when the
  person asks for it, through its own SMTP (Mailpit in development, a Tier 2 service).

## Consequences

Invariant 10 holds in its stated form: the shop validates tokens and never
authenticates anybody. What it gains is the right to *create* a person at the
provider, which any shop that imports customers needs and which is a provider's
administrative API, not an authorization flow.

The service account's secret is one more credential, kept as the others are
(AppHost user secrets, never the repository). The realm gains that client, password
reset, and an SMTP server.
