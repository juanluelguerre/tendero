import { InjectionToken, type Type } from '@angular/core';

/** The size contract (spec): S shows the figure, M the chart, L the chart and its table — S, M and L are 3, 6 and 12 of 12 columns. */
export type WidgetSize = 'S' | 'M' | 'L';

export type WidgetKey =
  | 'keyFigures'
  | 'salesByDay'
  | 'bestSellers'
  | 'byChannel'
  | 'waiting'
  | 'health'
  | 'ordersByStatus'
  | 'funnel'
  | 'returns'
  | 'stock'
  | 'customers'
  | 'agentSales'
  | 'refusals'
  | 'byCategory'
  | 'sellThrough'
  | 'campaigns'
  | 'searchStatus';

/** One widget on one dashboard, at one size: a dashboard is a list of these, which is what v2 will let a person edit. */
export interface WidgetPlacement {
  readonly widget: WidgetKey;
  readonly size: WidgetSize;
}

/** What the grid needs to draw a widget: its component, and whether it takes the dashboard's period. */
export interface WidgetDefinition {
  readonly component: Type<unknown>;
  readonly takesPeriod: boolean;
}

export const DASHBOARD_WIDGETS = new InjectionToken<Readonly<Record<WidgetKey, WidgetDefinition>>>('DASHBOARD_WIDGETS');
