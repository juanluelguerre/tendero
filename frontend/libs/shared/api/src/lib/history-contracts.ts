/**
 * A signed-in customer's trail (R-8, *Sigue donde lo dejaste* and *Tu historial*),
 * derived from the OpenAPI document. A guest's trail stays in the browser and is
 * never sent; a customer's is kept by Accounts under `/api/accounts/me/history`,
 * read and changed only by their own session (the `Shopper` policy).
 *
 * Every call answers the whole `BrowsingHistoryView` as it stands after the
 * change: most recent first, at most 50 products and 20 searches, nothing older
 * than 90 days, and `enabled: false` with empty lists once it is switched off.
 * At sign-in, after `POST /api/accounts/me`, the browser sends its guest trail
 * once with `MergeHistoryRequest` and clears it locally (docs/privacy.md).
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components, operations } from './generated/schema';

type Schemas = components['schemas'];

export type BrowsingHistoryView = Schemas['BrowsingHistoryView'];
export type ViewedProduct = Schemas['ViewedProduct'];
export type PastSearch = Schemas['PastSearch'];

export type RecordViewRequest = Schemas['RecordViewRequest'];
export type RecordSearchRequest = Schemas['RecordSearchRequest'];
export type MergeHistoryRequest = Schemas['MergeHistoryRequest'];
export type HistorySettingsRequest = Schemas['HistorySettingsRequest'];

/**
 * What one entry is, for `DELETE /api/accounts/me/history/{kind}/{key}` — the
 * key encoded with `encodeURIComponent`. From the document (ADR 0043).
 */
export type BrowsingKind = operations['ForgetBrowsingEntry']['parameters']['path']['kind'];
