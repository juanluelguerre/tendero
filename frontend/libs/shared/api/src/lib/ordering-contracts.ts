/**
 * Cart, checkout and returns contracts, derived from the OpenAPI document.
 *
 * Same arrangement as the others: `schema.ts` is generated and these aliases are
 * the stable surface the apps consume.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/**
 * The basket. **There is no money on it**, and that is not an omission: prices
 * are quoted live and frozen at order time (ADR 0016), so every figure a shopper
 * sees comes from a separate quote recomputed after each change.
 */
export type CartView = Schemas['CartView'];
export type CartLineView = Schemas['CartLineView'];

export type ShippingOptionsResponse = Schemas['ShippingOptionsResponse'];
export type ShippingOptionView = Schemas['ShippingOptionView'];

export type AddressRequest = Schemas['AddressRequest'];

/** An order as the confirmation page and the backoffice both read it. */
export type OrderView = Schemas['OrderView'];
export type OrderLineView = Schemas['OrderLineView'];
export type OrderDiscountView = Schemas['OrderDiscountView'];
export type OrderTaxView = Schemas['OrderTaxView'];

/**
 * What comes back on a 409 at checkout: the price moved between the quote and
 * the till, and the NEW fingerprint travels so the screen can show the
 * difference rather than just refusing.
 */
export type PriceChangedResponse = Schemas['PriceChangedResponse'];

/** An order and the returns opened against it — one page, one call. */
export type OrderDetail = Schemas['OrderDetail'];

/**
 * One row of the backoffice's orders table. A summary and not an `OrderView`:
 * thirty orders each carrying their lines, discounts and tax breakdown would be
 * a page that downloads a database to render six columns.
 */
export type OrderSummary = Schemas['OrderSummary'];

export type ReturnView = Schemas['ReturnView'];
export type ReturnLineView = Schemas['ReturnLineView'];

/**
 * Narrowed on the client, not promised by the document: both travel as `string`
 * because the server serialises the enum name by hand. Same decision, and same
 * caveat, as `ProductStatus` and `CombinationPolicy`.
 */
export type OrderStatus =
  | 'Pending'
  | 'PaymentAuthorized'
  | 'PaymentFailed'
  | 'Confirmed'
  | 'Shipped'
  | 'Delivered'
  | 'Cancelled';

export type ReturnStatus =
  | 'Requested'
  | 'Approved'
  | 'Received'
  | 'Refunded'
  | 'Rejected'
  | 'Cancelled';

/**
 * The closed set of return reasons. Closed on purpose: it is the input to the
 * return-reason analysis a later phase promises, and a text box would make that
 * a model guessing at what somebody typed.
 */
export const RETURN_REASONS = [
  'WrongSize',
  'NotAsDescribed',
  'Damaged',
  'WrongItem',
  'ChangedMind',
] as const;

export type ReturnReason = (typeof RETURN_REASONS)[number];
