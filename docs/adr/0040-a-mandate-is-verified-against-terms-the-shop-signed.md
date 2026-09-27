# ADR 0040 — A checkout mandate is verified against terms the shop itself signed

Status: accepted · 2026-09-27 · Related: [ADR 0039](0039-ucp-checkout-is-a-session-an-agent-owns.md), [ADR 0038](0038-a-test-order-is-a-mode-the-credential-decides.md), [ADR 0022 (reserved)](README.md) · Analysis: [`ucp-checkout-and-ap2.md`](../analysis/ucp-checkout-and-ap2.md) §3, §7, §9

## Context

UCP's AP2 extension (release 2026-08-25) binds a checkout to a person's approval
with two signatures. The shop signs every checkout it answers,
`ap2.merchant_authorization`: a detached JWS over the JCS bytes of the checkout
without `ap2`. The platform, once the person consents, signs a checkout mandate
over that signed checkout. It is an SD-JWT with key binding, in AP2 v0.2's shape
(`vct: mandate.checkout.1`, `checkout_jwt`, `checkout_hash`). The two documents
seem to disagree about what the mandate holds; analysis §9 shows they describe the
same bytes.

The plan said "a `fake` self-signed JWS as default". AP2 v0.2 made a fake
unnecessary and a real verifier possible without a real platform.

## Decision

1. **The shop's key is a port, `IMerchantSigner`**, published under the profile's
   `keys`. The development adapter makes a P-256 key at start-up and says so in a
   warning; production configures one (`Ucp:Signing:PrivateKeyPem`). Where a private
   key lives is an external system's business.
2. **Once AP2 is negotiated, every checkout is signed, and it is served as the
   very JSON that was signed.** Neither transport can serialise it into other bytes.
3. **Verification is the Trusted Platform Provider flow, in a fixed order, with one
   UCP code per failure.** The issuer key comes from the platform profile's `keys`
   (`agent_missing_key`). The issuer signature, the disclosures, the key-binding JWT
   against `cnf`, the hash and the `vct` (`mandate_invalid_signature`), then
   expiry (`mandate_expired`), then our own signature on the embedded checkout
   (`merchant_authorization_invalid`). Last, the terms against the session now
   (`mandate_scope_mismatch`).
4. **Terms are the goods and the money, not the words.** The comparison covers the
   session id, currency, line ids, SKUs, unit prices, quantities and every amount.
   A title in another language is the same purchase; a quantity is compared on its
   own, because a line discount can keep the money the same.
5. **Kinds of mandate are keyed by `vct`** (`ICheckoutMandateVerifier`). The closed
   mandate is the first; the open one is a class and a registration.
6. **A refusal is the current checkout, freshly signed, with the error.** The
   platform has what it needs to ask the person again.
7. **Development plays the platform.** `/dev-platforms/{agent}/profile.json`
   publishes a key per seeded agent. `/dev-platforms/{agent}/checkout-mandates`
   signs a real SD-JWT, and only over a checkout the shop signed. `?ap2=false`
   gives the same agent without AP2.
8. **No dependency.** JCS (RFC 8785, held to its own vectors), JWS with ECDSA and
   SD-JWT are written on `System.Security.Cryptography`.

## Consequences

- Not built: the Digital Payment Credential flow, which needs a list of trusted
  credential issuers. Open mandates and their constraints. The payment mandate,
  which is the PSP's to verify. What `aud` and `nonce` in the key-binding JWT
  should hold, which UCP leaves to the platform.
- The session is read, then completed: an update between the two is not seen by the
  mandate check. `P11-5` closed that: what verified travels on as a `MandateScope`,
  checked again where the order is placed, in the same transaction, and frozen onto it.
- An ephemeral key in development means a checkout signed before a restart comes
  back `merchant_authorization_invalid`, which is correct, and says why.
