import type { CheckoutFunnel, Money, OperationsSummary, SalesSeries, SalesSummary } from '@tendero/shared-api';
import { majorUnits } from '@tendero/shared-util';
import { type Change, changeOf } from '../figures';

export type KeyFigureKey =
  'revenue' | 'orders' | 'units' | 'averageOrder' | 'unitsPerOrder' | 'conversion' | 'returnRate';

export interface KeyFigure {
  readonly key: KeyFigureKey;
  readonly value: string;
  /** Against the previous period; null where the server has no previous period to compare with. */
  readonly change: Change | null;
  /** The figure step by step over the period, for its sparkline; null where there is no series. */
  readonly trend: readonly number[] | null;
}

export interface KeyFigureSources {
  readonly sales?: SalesSummary;
  readonly series?: SalesSeries;
  readonly funnel?: CheckoutFunnel;
  readonly operations?: OperationsSummary;
}

export interface KeyFigureFormats {
  readonly money: (amount: Money) => string;
  readonly share: (ratio: number) => string;
  /** A figure with decimals that is not money: units per order. */
  readonly decimal: (value: number) => string;
}

/**
 * The Overview's first row (spec, the Overview): the figures a shopkeeper asks
 * first, each with how it moved and its trend where both exist. Only saying
 * them: every number was computed by the server. Each source is optional
 * because each arrives on its own, and a figure appears when its source does.
 */
export function keyFigures(sources: KeyFigureSources, say: KeyFigureFormats): KeyFigure[] {
  const { sales, series, funnel, operations } = sources;
  const trend = (pick: (point: SalesSeries['current'][number]) => number) => series?.current.map(pick) ?? null;
  const figures: KeyFigure[] = [];

  if (sales) {
    const { current, previous } = sales;
    figures.push(
      {
        key: 'revenue',
        value: say.money(current.revenue),
        change: changeOf(current.revenue.amount, previous.revenue.amount),
        trend: trend((point) => majorUnits(point.revenue)),
      },
      {
        key: 'orders',
        value: String(current.orders),
        change: changeOf(current.orders, previous.orders),
        trend: trend((point) => point.orders),
      },
      {
        key: 'units',
        value: String(current.units),
        change: changeOf(current.units, previous.units),
        trend: trend((point) => point.units),
      },
      {
        key: 'averageOrder',
        value: say.money(current.averageOrderValue),
        change: changeOf(current.averageOrderValue.amount, previous.averageOrderValue.amount),
        trend: trend((point) => (point.orders === 0 ? 0 : majorUnits(point.revenue) / point.orders)),
      },
      {
        // How full a basket is, beside how dear (R-9, D-7).
        key: 'unitsPerOrder',
        value: say.decimal(current.unitsPerOrder),
        change: changeOf(current.unitsPerOrder, previous.unitsPerOrder),
        trend: trend((point) => (point.orders === 0 ? 0 : point.units / point.orders)),
      },
    );
  }

  // No previous period and no series for these two yet (Plan 3, D-7): said bare, never with an invented change.
  if (funnel) figures.push({ key: 'conversion', value: say.share(funnel.conversion), change: null, trend: null });
  if (operations)
    figures.push({ key: 'returnRate', value: say.share(operations.returnRate), change: null, trend: null });
  return figures;
}
