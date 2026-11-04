import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { SalesMix } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { ByChannelStore } from './by-channel.store';

/** Money as the API sends it: minor units and its currency. */
const eur = (amount: number) => ({ amount: Math.round(amount * 100), currency: 'EUR' });

const MIX: SalesMix = {
  period: 'Week',
  byChannel: [
    { channel: 'Web', orders: 6, revenue: eur(300) },
    { channel: 'Mcp', orders: 3, revenue: eur(150) },
    { channel: 'Ucp', orders: 1, revenue: eur(50) },
  ],
  unrecorded: { channel: null, orders: 2, revenue: eur(80) },
  byCategory: [],
};

/** By channel from the channel each order recorded (R-9, D-7): people against agents, and agents by MCP and UCP. */
describe('the by-channel store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        DashboardSources,
        DashboardRefresh,
        ByChannelStore,
      ],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  async function answered(mix: SalesMix) {
    const store = TestBed.inject(ByChannelStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush({});
    http.expectOne('http://api/api/dashboard/mix?period=Week').flush(mix);
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('shares the orders between the storefront and agents, and agents between MCP and UCP', async () => {
    const store = await answered(MIX);

    expect([store.chart()?.series.map((series) => series.values[0]), store.agentsBy(), store.agentShare()]).toEqual([
      [6, 4],
      { mcp: 3, ucp: 1 },
      0.4,
    ]);
  });

  /**
   * A Shopify store Tendero sells through (phase 16, S3): its buyers are people, so
   * they count with the storefront's, and the two are told apart in words.
   */
  it('counts the people who bought on Shopify with the storefront and says how many', async () => {
    const store = await answered({
      ...MIX,
      byChannel: [...MIX.byChannel, { channel: 'Shopify', orders: 2, revenue: eur(100) }],
    });

    expect([store.chart()?.series.map((series) => series.values[0]), store.peopleBy(), store.agentShare()]).toEqual([
      [8, 4],
      { web: 6, shopify: 2 },
      4 / 12,
    ]);
  });

  /** Orders placed before the channel was recorded are their own line — never counted as the storefront's. */
  it('keeps the orders before channels apart', async () => {
    const store = await answered(MIX);

    expect(store.unrecorded()).toBe(2);
  });
});
