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

/**
 * An agent's checkout handed to a person (UCP's requires_escalation, P11-1): the
 * cart's token to take over, and the language the agent checked out in.
 */
export type CheckoutHandOff = Schemas['CheckoutHandOff'];

/** What checkout sends: the address, the chosen rate, the quote's fingerprint and the idempotency key. */
export type PlaceOrderRequest = Schemas['PlaceOrderRequest'];

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
 * How many of a SKU may still be sent back: the domain's own answer, so the page
 * never keeps a second copy of which returns count.
 */
export type ReturnableLineView = Schemas['ReturnableLineView'];

/**
 * One row of the backoffice's orders table. A summary and not an `OrderView`:
 * thirty orders each carrying their lines, discounts and tax breakdown would be
 * a page that downloads a database to render six columns.
 */
export type OrderSummary = Schemas['OrderSummary'];

/** The orders list with a count for every status, zero included (the redesign's R-4.2). */
export type ListOrdersResult = Schemas['ListOrdersResult'];
export type OrderStatusCount = Schemas['OrderStatusCount'];

export type RequestReturnRequest = Schemas['RequestReturnRequest'];
export type ReturnLineRequest = Schemas['ReturnLineRequest'];

/**
 * What the shop promises about sending something back, published so that no
 * screen has to know the number. It is days from DELIVERY, which is what the
 * aggregate measures, and not from the purchase.
 */
export type ReturnPolicy = Schemas['ReturnPolicy'];

export type ReturnView = Schemas['ReturnView'];
export type ReturnLineView = Schemas['ReturnLineView'];

/** From the document (ADR 0043). */
export type OrderStatus = Schemas['OrderStatus'];
export type OrderMode = Schemas['OrderMode'];
export type ReturnStatus = Schemas['ReturnStatus'];
export type DashboardPeriod = Schemas['DashboardPeriod'];
export type OrderMove = Schemas['OrderMove'];
export type ReturnDecision = Schemas['ReturnDecision'];

/**
 * The closed set of return reasons. Closed on purpose: it is the input to the
 * return-reason analysis a later phase promises, and a text box would make that
 * a model guessing at what somebody typed.
 */
export type ReturnReason = Schemas['ReturnReason'];

/** The set as a list, for a picker; `satisfies` holds it to the document's. */
export const RETURN_REASONS = [
  'WrongSize',
  'NotAsDescribed',
  'Damaged',
  'WrongItem',
  'ChangedMind',
] as const satisfies readonly ReturnReason[];

/** A shopper's own orders. A different endpoint from the shopkeeper's list, and
 *  deliberately so: that one is allowed to return everything and this one is not. */
export type MyOrders = Schemas['MyOrdersResult'];
export type LinkedIdentity = Schemas['LinkedIdentity'];

/**
 * The shopkeeper's dashboard, Ordering's half (P11-7): one figure per endpoint,
 * each widget asking for exactly what it draws. Rehearsals are left out of every
 * figure but the agents' own count of them.
 */
export type SalesSummary = Schemas['SalesSummary'];
/** Sales by hour or by day on the shop's clock, and the period before (R-9, ADR 0046). */
export type SalesSeries = Schemas['SalesSeries'];
export type SalesPoint = Schemas['SalesPoint'];
export type SeriesStep = Schemas['SeriesStep'];
export type SalesFigures = Schemas['SalesFigures'];
export type BestSeller = Schemas['BestSeller'];
export type OperationsSummary = Schemas['OperationsSummary'];
export type CheckoutFunnel = Schemas['CheckoutFunnel'];
export type AgentSales = Schemas['AgentSales'];
export type AgentSalesRow = Schemas['AgentSalesRow'];
export type OrderMandateSummary = Schemas['OrderMandateSummary'];
