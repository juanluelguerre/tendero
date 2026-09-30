/**
 * Inventory contracts, derived from the OpenAPI document.
 *
 * Same arrangement as the catalogue and pricing ones: `schema.ts` is generated
 * and these aliases are the stable surface the apps consume.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/**
 * One shelf: a SKU in a warehouse. `available` is derived server-side
 * (`onHand - reserved`) and is not something the client should recompute — a
 * grid that did its own subtraction would disagree with the search index the
 * moment a reservation expired.
 */
export type StockRow = Schemas['StockRowView'];

/**
 * A hold, or the record of one that was refused. `reason` is the interesting
 * field and it is only ever set on a resolved reservation: it is what turns
 * "the order was cancelled" into "PANS: 2 asked for, 1 available".
 */
export type ReservationRow = Schemas['ReservationView'];

export type StockList = Schemas['ListStockResult'];

/** The stock as the dashboard reads it: coverage at the shop's pace and sell-through (R-9, D-7). */
export type StockHealth = Schemas['StockHealth'];
export type SkuCoverage = Schemas['SkuCoverage'];
export type SkuSellThrough = Schemas['SkuSellThrough'];
export type WarehouseCoverage = Schemas['WarehouseCoverage'];

/** The counted row, flat, so the grid can replace the one it is showing. */
export type CountedStock = Schemas['CountStockResponse'];

/** From the document (ADR 0043). */
export type ReservationStatus = Schemas['ReservationStatus'];
export type StockFilter = Schemas['StockFilter'];
