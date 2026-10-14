import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { SalesSeries } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { SalesByDayWidget } from './sales-by-day-widget';

/** Money as the API sends it: minor units and its currency. */
const eur = (amount: number) => ({ amount: Math.round(amount * 100), currency: 'EUR' });

const point = (start: string, revenue: number) => ({
  start,
  revenue: eur(revenue),
  orders: 1,
  units: 1,
  agentOrders: 0,
  agentRevenue: eur(0),
  newCustomers: 1,
  returningCustomers: 0,
});

const WEEK: SalesSeries = {
  period: 'Week',
  timeZone: 'Europe/Madrid',
  step: 'Day',
  current: [point('2026-10-12T00:00:00+02:00', 520)],
  previous: [point('2026-10-05T00:00:00+02:00', 470)],
};

const words = (current: string, previous: string) => ({
  dashboard: { salesByDay: { title: 'Sales', current: { Week: current }, previous: { Week: previous } } },
});

describe('the sales by day widget', () => {
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
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  const flushLanguage = (language: string, translations: object) => {
    for (const request of http.match((req) => req.url.includes(`/i18n/${language}`))) request.flush(translations);
  };

  /** The legend names each series; a label read once would stay in the language the page opened in. */
  it('renames its series when the language changes', async () => {
    const culture = TestBed.inject(CultureStore);
    culture.use('en');
    const fixture = TestBed.createComponent(SalesByDayWidget);
    fixture.componentRef.setInput('period', 'Week');
    fixture.detectChanges();
    flushLanguage('en', words('Last 7 days', 'The 7 days before'));
    http.expectOne((req) => req.url === 'http://api/api/dashboard/sales/series').flush(WEEK);
    await fixture.whenStable();
    fixture.detectChanges();
    const legend = () =>
      [...(fixture.nativeElement as HTMLElement).querySelectorAll('.legend li')].map((item) =>
        item.textContent?.trim(),
      );

    expect(legend()).toEqual(['Last 7 days', 'The 7 days before']);

    culture.use('es');
    flushLanguage('es', words('Últimos 7 días', 'Los 7 días anteriores'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(legend()).toEqual(['Últimos 7 días', 'Los 7 días anteriores']);
  });

  /**
   * The series ends now and reaches back a whole window — the seven days up to
   * today, not the calendar week — so "this week" would name a different set
   * of days from the one drawn.
   */
  it('names a window that ends now, in both languages', () => {
    const labels = (language: string) =>
      JSON.parse(readFileSync(join(process.cwd(), 'apps', 'backoffice', 'public', 'i18n', `${language}.json`), 'utf-8'))
        .dashboard.salesByDay;

    expect([labels('en').current, labels('en').previous]).toEqual([
      { Day: 'Last 24 hours', Month: 'Last 30 days', Week: 'Last 7 days' },
      { Day: 'The 24 hours before', Month: 'The 30 days before', Week: 'The 7 days before' },
    ]);
    expect([labels('es').current, labels('es').previous]).toEqual([
      { Day: 'Últimas 24 horas', Month: 'Últimos 30 días', Week: 'Últimos 7 días' },
      { Day: 'Las 24 horas anteriores', Month: 'Los 30 días anteriores', Week: 'Los 7 días anteriores' },
    ]);
  });
});
