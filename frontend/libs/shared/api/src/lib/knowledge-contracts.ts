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
export type QuoteForReview = Schemas['QuoteForReview'];
export type ReviewClaimResponse = Schemas['ReviewClaimResponse'];
