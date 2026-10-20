/**
 * The shopper's own account beyond the address book and the wallet (P15-6, P15-9;
 * docs/specs/shopper-account.md), derived from the OpenAPI document. Read and
 * changed only by the shopper's own session.
 *
 * Preferences: the language the shop speaks when a request names no other, and
 * one marketing consent with its date and the wording's version — off until
 * given. No currency, and no analytics consent, which stays in the browser.
 *
 * The shopper's data: `GET /api/accounts/me/data` is one download of what the shop
 * holds, each context's share by its section; `DELETE /api/accounts/me` removes
 * the account, answering what was forgotten and what is kept, and for how long.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

export type PreferencesView = Schemas['PreferencesView'];
export type MarketingConsentView = Schemas['MarketingConsentView'];
export type PersonalDataExport = Schemas['PersonalDataExport'];
export type AccountRemovalView = Schemas['AccountRemovalView'];
