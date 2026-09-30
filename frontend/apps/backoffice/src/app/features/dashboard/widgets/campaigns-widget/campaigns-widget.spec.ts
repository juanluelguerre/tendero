import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { BarChartData } from '../../../../charts/chart-contract';
import { English, setUpWidgets } from '../widget-testing';
import { CampaignsWidget } from './campaigns-widget';

const CALENDAR = [
  {
    code: 'SPRING',
    name: { es: 'Primavera', en: 'Spring' },
    scope: 'Shop',
    categoryCode: null,
    startsAt: '2026-09-30T11:00:00+00:00',
    endsAt: '2026-10-07T11:00:00+00:00',
    priority: 10,
    status: 'Scheduled',
    phase: 'Running',
    endedAt: null,
  },
];

/** A shop in Auckland: its 1 October starts at 11:00 UTC on the 30th, which is still the 30th in Madrid. */
const UPLIFT = {
  currency: 'EUR',
  timeZone: 'Pacific/Auckland',
  hasBaseline: true,
  sold: 100,
  baseline: 80,
  uplift: 20,
  days: [
    { start: '2026-09-30T11:00:00+00:00', sold: 100, baseline: 80, future: false, partial: false },
    { start: '2026-10-01T11:00:00+00:00', sold: 10, baseline: 90, future: false, partial: true },
    { start: '2026-10-02T11:00:00+00:00', sold: 0, baseline: 85, future: true, partial: false },
  ],
};

/** A campaign against its baseline (R-9, D-7), drawn in the shop's days and never with today's half-day. */
describe('the campaigns widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  it("labels the days in the shop's zone and leaves today, under way, out of the chart", async () => {
    // Its own opening, not openWidgetFixture's: the uplift is asked only once the
    // calendar has answered, and waiting for stability would wait for it.
    const fixture = TestBed.createComponent(CampaignsWidget);
    const http = TestBed.inject(HttpTestingController);
    const settle = async () => {
      fixture.detectChanges();
      await new Promise((settled) => setTimeout(settled));
      TestBed.tick();
    };
    await settle();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush(English);
    http.expectOne('http://api/api/pricing/campaign-calendar').flush(CALENDAR);
    await settle();
    http.expectOne((req) => req.url === 'http://api/api/dashboard/uplift').flush(UPLIFT);
    await settle();
    const chart = (fixture.componentInstance as unknown as { chart: () => BarChartData | null }).chart();

    expect(chart?.categories).toEqual(['Oct 1']);
    expect(chart?.series.map((series) => series.values)).toEqual([[100], [80]]);
  });
});
