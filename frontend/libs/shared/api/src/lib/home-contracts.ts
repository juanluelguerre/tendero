/**
 * The home's rows (R-8), derived from the OpenAPI document.
 *
 * - *Lo más vendido* and *Tendencias* answer `RankedProducts`: a rank and a
 *   product id, never a count — the storefront resolves the cards with
 *   `GET /api/search/cards?ids=…`, which answers them in the order asked and
 *   leaves out a product that can no longer be shown. An empty `items` draws no row.
 * - *Puede que te guste* answers `SimilarProductsResult`: each card with
 *   `becauseOf`, the code of the viewed product it comes from, so *Porque viste …*
 *   is data, not wording; `rankedBy` says whether the rule alone or the vector
 *   re-rank ordered it (the spec's evidence of use).
 * - `ProductCardsResult` resolves a list of ids or codes — the trail's codes
 *   too — into search cards.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

export type RankedProduct = Schemas['RankedProduct'];
export type RankedProducts = Schemas['RankedProducts'];

export type SimilarProduct = Schemas['SimilarProduct'];
export type SimilarProductsResult = Schemas['SimilarProductsResult'];

/** From the document (ADR 0043). A client keeps a branch for a value it does not know yet. */
export type SimilarityRanking = Schemas['SimilarityRanking'];

export type ProductCardsResult = Schemas['ProductCardsResult'];
