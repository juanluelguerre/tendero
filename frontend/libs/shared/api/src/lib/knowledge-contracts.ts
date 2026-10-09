/**
 * Knowledge contracts, derived from the OpenAPI document (ADR 0018).
 *
 * Same arrangement as the other contexts: `schema.ts` is generated and these
 * aliases are the stable surface the apps consume.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/**
 * A claim waiting for a person. The server orders the queue — weakest support
 * first — and computes the support as the claim's weakest evidence; the screen
 * shows both and recomputes neither.
 */
export type ClaimForReview = Schemas['ClaimForReview'];

/**
 * A doubt about a picture (phase 13a, I-4d): the model is sure it shows another
 * colour than the catalogue holds. Not a claim — it says something of the
 * picture, and a person says what.
 */
export type PictureDoubtView = Schemas['PictureDoubtView'];
export type PictureDoubtsPage = Schemas['PictureDoubtsPage'];
export type PictureDoubtDecision = Schemas['PictureDoubtDecision'];
export type DecidePictureDoubtResponse = Schemas['DecidePictureDoubtResponse'];
export type ClaimsForReviewPage = Schemas['ClaimsForReviewPage'];
export type ClaimSupportFilter = Schemas['ClaimSupportFilter'];
export type QuoteForReview = Schemas['QuoteForReview'];
export type ReviewClaimResponse = Schemas['ReviewClaimResponse'];

/**
 * A product's claims approved together (Steward, S-5): one row per claim, and a
 * weakly supported one refused on its row by the server, whatever was sent.
 */
export type ClaimApprovalsResponse = Schemas['ClaimApprovalsResponse'];
export type ClaimApprovalView = Schemas['ClaimApprovalView'];
export type BulkApprovalOutcome = Schemas['BulkApprovalOutcome'];

/**
 * The specs of one to four products, aligned row by row (P10-7, P10-8). One
 * product is the product page's table, several a comparison: the same rows, so
 * the two never disagree. A cell's `source` is `catalogue`, `description` (an
 * approved claim put the value there, and `evidence` holds its words) or `none`
 * — no data, which the interface says in its own words, never a guess.
 */
export type SpecSheet = Schemas['SpecSheet'];
export type SpecProduct = Schemas['SpecProduct'];
export type SpecRow = Schemas['SpecRow'];
export type SpecCell = Schemas['SpecCell'];
export type FactKind = Schemas['FactKind'];
export type SpecEvidence = Schemas['SpecEvidence'];
export type SpecQuote = Schemas['SpecQuote'];
export type SpecStatement = Schemas['SpecStatement'];

/** The three values `SpecCell.source` takes; the server's `SpecSources`. */
export type SpecSource = 'catalogue' | 'description' | 'none';
