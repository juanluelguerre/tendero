import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CustomersWidget } from './customers-widget';
import { openWidget, setUpWidgets } from '../widget-testing';

const FUNNEL = (newCustomers: number, returningCustomers: number) => ({
  period: 'Week',
  cartsStarted: 10,
  checkedOut: 8,
  abandoned: 2,
  stillOpen: 0,
  conversion: 0.8,
  newCustomers,
  returningCustomers,
});

const point = (start: string, newCustomers: number, returningCustomers: number) => ({
  start,
  revenue: 0,
  orders: 0,
  units: 0,
  agentOrders: 0,
  agentRevenue: 0,
  newCustomers,
  returningCustomers,
});

/** The buyers day by day (R-9, D-7), from the series the key figures already read. */
const SERIES = (newCustomers: number, returningCustomers: number) => ({
  period: 'Week',
  currency: 'EUR',
  timeZone: 'Europe/Madrid',
  step: 'Day',
  current: [
    point('2026-10-12T00:00:00+02:00', newCustomers, returningCustomers),
    point('2026-10-13T00:00:00+02:00', 0, 0),
  ],
  previous: [point('2026-10-05T00:00:00+02:00', 0, 0), point('2026-10-06T00:00:00+02:00', 0, 0)],
});

const open = (funnel: object, series: object) =>
  openWidget(CustomersWidget, { '/api/dashboard/funnel': funnel, '/api/dashboard/sales/series': series });

describe('the customers widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  it('splits the buyers into new and returning, as figures and day by day', async () => {
    const element = await open(FUNNEL(6, 2), SERIES(6, 2));

    expect([...element.querySelectorAll('.fact__value')].map((value) => value.textContent?.trim())).toEqual(['6', '2']);
    expect(element.querySelector('backoffice-bar-chart')).not.toBeNull();
    expect([...element.querySelectorAll('.legend li')].map((item) => item.textContent?.trim())).toEqual([
      'New',
      'Returning',
    ]);
  });

  it('says nobody bought rather than drawing a share of nobody', async () => {
    const element = await open(FUNNEL(0, 0), SERIES(0, 0));

    expect(element.querySelector('backoffice-bar-chart')).toBeNull();
    expect(element.textContent).toContain('Nobody bought in this period.');
  });
});
