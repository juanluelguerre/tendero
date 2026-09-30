import type { SalesSeries } from '@tendero/shared-api';
import type { BarChartData } from '../../../../charts/chart-contract';

/**
 * The series as the chart draws it (R-9): each bucket labelled in the reader's
 * culture and in the shop's zone, which the server bucketed it in (ADR 0046) — an hour for a day, a
 * weekday for a week, a date for a month.
 */
export function toBarChart(series: SalesSeries, culture: string, labels: { current: string; previous: string }): BarChartData {
  return {
    categories: bucketLabels(series, culture),
    series: [
      { key: 'current', label: labels.current, role: 'data', values: series.current.map((point) => point.revenue) },
      { key: 'previous', label: labels.previous, role: 'previous', values: series.previous.map((point) => point.revenue) },
    ],
  };
}

/** Each bucket of the current period, named in the reader's culture and the shop's zone: every chart drawn from the series uses these. */
export function bucketLabels(series: SalesSeries, culture: string): string[] {
  const format = new Intl.DateTimeFormat(culture, labelOptions(series));
  return series.current.map((point) => format.format(new Date(point.start)).replace('.', ''));
}

function labelOptions(series: SalesSeries): Intl.DateTimeFormatOptions {
  const timeZone = series.timeZone;
  if (series.step === 'Hour') return { timeZone, hour: '2-digit', hourCycle: 'h23' };
  if (series.period === 'Week') return { timeZone, weekday: 'short' };
  return { timeZone, day: 'numeric', month: 'short' };
}
