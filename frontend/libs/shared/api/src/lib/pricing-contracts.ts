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
 * Money as every amount in the contract is written (phase 13b): a whole number
 * of the currency's **minor units** and the currency — 113,90 € is
 * `{ amount: 11390, currency: 'EUR' }`. Never divided by a hundred by hand:
 * `formatPrice` and the `price` pipe turn it into text, and `majorUnits` into
 * a number for a chart.
 */
export type Money = Schemas['Money'];

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

/** From the document (ADR 0043). */
export type CombinationPolicy = Schemas['CombinationPolicy'];
export type DiscountOutcome = Schemas['DiscountOutcome'];

/**
 * A promotion as a SHOPPER may see it, which is a different type from
 * `PromotionView` above and deliberately so: no coupon code, no segment, no
 * priority, no combination rule. The server decides what reaches the wire; this
 * alias only names it.
 */
export type OfferView = Schemas['OfferView'];
export type OfferList = Schemas['ListOffersResult'];
