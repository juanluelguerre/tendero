/**
 * The contract for `GET /api/search`, **derived** from the OpenAPI document.
 *
 * Same as the catalogue ones: it was a hand mirror of `Search.Contracts` and now
 * comes out of `docs/openapi/tendero.json`, with a test that stops the document
 * and the API from parting ways.
 *
 * `imageId` is still the key in the store and not a URL: the client composes it
 * as `/api/images/{imageId}` so that changing CDN is not an UPDATE over millions
 * of rows (ADR 0011).
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

export type SearchHit = Schemas['SearchHit'];
export type SearchResultPage = Schemas['SearchResultPage'];

/**
 * What kind of answer a page is (ADR 0032), decided with no model: `Exact` when
 * some product carries every word, `Related` when the results are only the
 * closest things. Absent for a browse or a date order — there is nothing to judge.
 */
export type SearchAnswer = Schemas['SearchAnswer'];

/**
 * `GET /api/search/evaluation`: the committed golden sets asked of both
 * strategies against the running shop (P8-11).
 */
export type SearchEvaluation = Schemas['SearchEvaluation'];
export type CultureEvaluation = Schemas['CultureEvaluation'];
export type StrategyScore = Schemas['StrategyScore'];
export type QueryEvaluation = Schemas['QueryEvaluation'];
