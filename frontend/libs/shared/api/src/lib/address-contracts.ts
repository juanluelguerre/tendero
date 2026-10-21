/**
 * A signed-in customer's address book (P15-2, docs/specs/shopper-account.md),
 * derived from the OpenAPI document. Kept by Accounts under
 * `/api/accounts/me/addresses`, read and changed only by the shopper's own
 * session (the `Shopper` policy) — never by an agent.
 *
 * Every call answers the whole `AddressBookView` as it stands after the change:
 * oldest first, at most `maxEntries`, each entry saying whether it is the default
 * for delivery and for billing. Removing a default leaves none; nothing is
 * promoted. A refusal is a problem with a code: `AddressBookFull`,
 * `UnknownAddress`, `InvalidAddress` or `NoCustomer`.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components, operations } from './generated/schema';

type Schemas = components['schemas'];

export type AddressBookView = Schemas['AddressBookView'];
export type AddressEntryView = Schemas['AddressEntryView'];
export type SavedAddress = Schemas['SavedAddress'];
export type AddressEntryRequest = Schemas['AddressEntryRequest'];

/** What a default is for, for `PUT /api/accounts/me/addresses/{id}/default/{use}`. From the document (ADR 0043). */
export type AddressUse = operations['UseAddressByDefault']['parameters']['path']['use'];
