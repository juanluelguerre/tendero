/**
 * Pricing contracts, derived from the OpenAPI document.
 *
 * Same arrangement as the catalogue ones: `schema.ts` is generated, and these
 * aliases are the stable surface the apps consume. If a type disappears from the
 * document, the line that aliases it stops compiling — which is the warning that
 * hand-written mirrors never gave.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/**
 * A promotion as the shopkeeper sees it. The combination column is the reason
 * the screen exists: the question is not "how much is this worth" but "if I add
 * it, what stops applying".
 */
export type PromotionView = Schemas['PromotionView'];
export type PromotionList = Schemas['ListPromotionsResult'];

export type PriceQuote = Schemas['QuoteResponse'];
export type QuotedLine = Schemas['QuotedLineResponse'];
export type TaxLine = Schemas['TaxLineResponse'];

/**
 * A promotion the engine evaluated — applied OR suppressed, and a suppressed
 * one always carries its reason. That is what lets the cart say "not combinable
 * with Summer sale" instead of silently showing one discount fewer.
 */
export type AppliedDiscount = Schemas['AppliedDiscountResponse'];

/**
 * Narrowed on the client, not promised by the document: both travel as `string`
 * because the server serialises the enum name by hand. Same decision, and same
 * caveat, as `ProductStatus` in the catalogue contracts.
 */
export type CombinationPolicy = 'ExclusiveGlobal' | 'ExclusiveInGroup' | 'Stackable';
export type DiscountOutcome = 'Applied' | 'Suppressed';
