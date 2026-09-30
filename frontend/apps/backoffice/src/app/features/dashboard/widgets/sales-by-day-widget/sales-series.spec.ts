import type { SalesSeries } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { toBarChart } from './sales-series';

const point = (start: string, revenue: number) => ({
  start,
  revenue,
  orders: 1,
  units: 1,
  agentOrders: 0,
  agentRevenue: 0,
  newCustomers: 1,
  returningCustomers: 0,
});

const WEEK: SalesSeries = {
  period: 'Week',
  currency: 'EUR',
  timeZone: 'Europe/Madrid',
  step: 'Day',
  current: [point('2026-10-12T00:00:00+02:00', 520), point('2026-10-13T00:00:00+02:00', 930)],
  previous: [point('2026-10-05T00:00:00+02:00', 470), point('2026-10-06T00:00:00+02:00', 720)],
};

describe('a sales series as a chart', () => {
  it('labels each day in the reader culture and zone, and keeps the period before as its own series', () => {
    const chart = toBarChart(WEEK, 'en', { current: 'This week', previous: 'Week before' });

    expect(chart.categories).toEqual(['Mon', 'Tue']);
    expect(chart.series.map((series) => [series.role, series.values])).toEqual([
      ['data', [520, 930]],
      ['previous', [470, 720]],
    ]);
  });

  it('labels a day by its hours', () => {
    const day: SalesSeries = { ...WEEK, period: 'Day', step: 'Hour', current: [point('2026-10-14T06:00:00Z', 10)], previous: [point('2026-10-13T06:00:00Z', 5)] };

    expect(toBarChart(day, 'es', { current: 'Hoy', previous: 'Ayer' }).categories).toEqual(['08']);
  });

  it('labels a month by its dates', () => {
    const month: SalesSeries = { ...WEEK, period: 'Month' };

    expect(toBarChart(month, 'es', { current: 'Mes', previous: 'Anterior' }).categories).toEqual(['12 oct', '13 oct']);
  });
});
