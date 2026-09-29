import { InjectionToken } from '@angular/core';

/**
 * What every chart in the backoffice takes (R-9): the data, how to say a value,
 * and a caption. The engine behind it — our own SVG or Apache ECharts — is a
 * detail the contract hides, so the two can be compared on the same chart and
 * one of them removed without touching a widget.
 */
export type SeriesRole = 'data' | 'previous' | 'agent';

export interface BarSeries {
  readonly key: string;
  readonly label: string;
  readonly role: SeriesRole;
  readonly values: readonly number[];
}

export interface BarChartData {
  readonly categories: readonly string[];
  readonly series: readonly BarSeries[];
}

export type ValueFormat = (value: number) => string;

export type ChartEngine = 'svg' | 'echarts';

/** Which engine draws: SVG unless the comparison asks for the other. */
export const CHART_ENGINE = new InjectionToken<ChartEngine>('CHART_ENGINE', { factory: () => 'svg' });

/** A series' colour is its role's token, never a hex: `tokens.css` owns them. */
export function roleColourToken(role: SeriesRole): string {
  return role === 'data' ? '--chart-data' : role === 'agent' ? '--chart-agent' : '--chart-previous';
}
