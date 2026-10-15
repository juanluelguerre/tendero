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
import type { components, operations } from './generated/schema';

type Schemas = components['schemas'];

export type SearchHit = Schemas['SearchHit'];

/**
 * The orders `GET /api/search?sort=` keeps, as the contract declares them
 * (ADR 0043: a closed set a client SENDS is declared too). A query parameter's
 * set lives on its operation, not among the schemas.
 */
export type ProductSort = NonNullable<NonNullable<operations['SearchProducts']['parameters']['query']>['sort']>;
export type SearchResultPage = Schemas['SearchResultPage'];

/**
 * The filters a listing can be narrowed by, each with how much every choice
 * would leave (the storefront's facets, F-2). Returned only when they split
 * the results, which is the server's rule and not the screen's.
 */
export type SearchFacet = Schemas['SearchFacet'];
export type FacetKind = Schemas['FacetKind'];
export type FacetOption = Schemas['FacetOption'];
export type PriceFacetRange = Schemas['PriceFacetRange'];
export type PriceBand = Schemas['PriceBand'];

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

/**
 * Searching with a picture (phase 13a): the products whose pictures are
 * nearest, each with the picture that matched — its id, composed into a URL as
 * a card's is — and whether this shop can be asked at all.
 */
export type VisualSearchResult = Schemas['VisualSearchResult'];
export type VisualMatch = Schemas['VisualMatch'];
export type VisualSearchAvailability = Schemas['VisualSearchAvailability'];

/** Search's stages, their state and since when, for the dashboard (R-9, D-7). */
export type SearchStatus = Schemas['SearchStatus'];
export type StageStatus = Schemas['StageStatus'];
export type SearchStage = Schemas['SearchStage'];
export type StageState = Schemas['StageState'];

/**
 * A kit (phase 13c): a template the shop wrote, and what the solver made of
 * it for a budget — one product a slot, an empty slot with its reason, and
 * the solver's own sentences for a reader with no page.
 */
export type KitTemplate = Schemas['KitTemplateView'];
export type KitView = Schemas['KitView'];
export type KitSlotView = Schemas['KitSlotView'];
export type BuildKitRequest = Schemas['BuildKitRequest'];
export type KitSentenceUnderstood = Schemas['KitSentenceUnderstood'];
export type KitSentenceAvailability = Schemas['KitSentenceAvailability'];
