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
import { ByCategoryStore } from './by-category.store';

const MIX: SalesMix = {
  period: 'Week',
  currency: 'EUR',
  byChannel: [],
  unrecorded: { channel: null, orders: 0, revenue: 0 },
  byCategory: [
    { categoryCode: 'COOKWARE', units: 4, revenue: 400 },
    { categoryCode: 'MUGS', units: 9, revenue: 90 },
    { categoryCode: 'LAMPS', units: 1, revenue: 60 },
    { categoryCode: 'BAGS', units: 1, revenue: 50 },
    { categoryCode: 'TEXTILES', units: 1, revenue: 40 },
    { categoryCode: 'RUGS', units: 1, revenue: 30 },
    { categoryCode: null, units: 2, revenue: 25 },
  ],
};

/** Sales by category (R-9, D-7): the five that sold most, named in the reader's language, the rest together, and the uncategorised apart. */
describe('the by-category store', () => {
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
        ByCategoryStore,
      ],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  it('ranks the five that sold most by name, puts the rest together, and keeps the uncategorised apart', async () => {
    const store = TestBed.inject(ByCategoryStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/')))
      request.flush({ dashboard: { byCategory: { others: 'Others', uncategorised: 'No category' } } });
    http.expectOne('http://api/api/dashboard/mix?period=Week').flush(MIX);
    http
      .expectOne((req) => req.url === 'http://api/api/catalog/categories')
      .flush({
        items: [{ code: 'COOKWARE', parent: null, name: 'Cookware', path: [], depth: 0 }],
      });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.rows().map((row) => [row.label, row.revenue])).toEqual([
      ['Cookware', 400],
      ['MUGS', 90],
      ['LAMPS', 60],
      ['BAGS', 50],
      ['TEXTILES', 40],
      ['Others', 30],
      ['No category', 25],
    ]);
  });
});
