/**
 * What every chart in the backoffice takes (R-9): the data, how to say a value,
 * and a caption. The engine behind it is a detail the contract hides: our own SVG
 * and Apache ECharts were compared behind it on the same chart, ECharts stayed
 * (ADR 0045), and the one that left touched no widget.
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

/** A series' colour is its role's token, never a hex: `tokens.css` owns them. */
export function roleColourToken(role: SeriesRole): string {
  return role === 'data' ? '--chart-data' : role === 'agent' ? '--chart-agent' : '--chart-previous';
}
