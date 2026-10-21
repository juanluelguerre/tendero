/**
 * A signed-in customer's saved ways to pay (P15-4, ADR 0064), derived from the
 * OpenAPI document. Kept by Ordering under `/api/accounts/me/payment-methods`,
 * read and changed only by the shopper's own session.
 *
 * **No token ever reaches the browser.** A method is what may be shown — brand,
 * last four, expiry, whether it has expired — and an id the shop minted, which is
 * what checkout sends (`PlaceOrderRequest.savedMethodId`) to charge it. A card is
 * kept from what the provider's own field produced (`KeepCardRequest.instrument`),
 * never from a number. Refusals: `WalletFull`, `CardRefused`,
 * `PaymentProviderUnavailable`, `UnknownPaymentMethod`, `NoCustomer`.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

export type WalletView = Schemas['WalletView'];
export type SavedMethodView = Schemas['SavedMethodView'];
export type KeepCardRequest = Schemas['KeepCardRequest'];
