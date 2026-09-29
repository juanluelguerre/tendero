import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { DashboardPage } from './dashboard-page';
import { dashboardChildren } from './dashboard-routes';
import { DASHBOARD_WIDGETS, type WidgetDefinition, type WidgetKey } from './dashboard-widgets';
import { English } from './widget-testing';

@Component({ selector: 'backoffice-stub-widget', template: '{{ period() }}' })
class StubWidget {
  readonly period = input.required<string>();
}

const Keys: WidgetKey[] = [
  'keyFigures', 'salesByDay', 'bestSellers', 'byChannel', 'waiting', 'health', 'ordersByStatus',
  'funnel', 'returns', 'stock', 'customers', 'agentSales', 'refusals',
];
const Stubs = Object.fromEntries(Keys.map((key) => [key, { component: StubWidget, takesPeriod: true }])) as Record<WidgetKey, WidgetDefinition>;

describe('the dashboard page', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        provideRouter([{ path: 'dashboard', component: DashboardPage, children: dashboardChildren }]),
      ],
    });
    TestBed.overrideComponent(DashboardPage, { add: { providers: [{ provide: DASHBOARD_WIDGETS, useValue: Stubs }] } });
    TestBed.inject(CultureStore).use('en');
  });

  async function open(url: string) {
    const harness = await RouterTestingHarness.create(url);
    for (const request of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/'))) request.flush(English);
    await harness.fixture.whenStable();
    harness.detectChanges();
    return harness;
  }

  const shell = (harness: RouterTestingHarness) => harness.fixture.nativeElement as HTMLElement;

  it('opens the tab its URL names, on the period its URL names', async () => {
    const element = shell(await open('/dashboard/sales?period=month'));

    expect(element.querySelector('nav.tabs a[aria-current="page"]')?.getAttribute('href')).toBe('/dashboard/sales?period=month');
    expect(element.querySelector('.period[aria-pressed="true"]')?.getAttribute('data-period')).toBe('Month');
    expect([...element.querySelectorAll<HTMLElement>('.cell')].map((cell) => cell.dataset['widget'])).toEqual([
      'keyFigures', 'salesByDay', 'byChannel', 'bestSellers',
    ]);
    expect(element.querySelector('.cell')?.textContent?.trim()).toBe('Month');
  });

  it('opens the Overview at /dashboard, on the week', async () => {
    const element = shell(await open('/dashboard'));

    expect(element.querySelector('nav.tabs a[aria-current="page"]')?.getAttribute('href')).toBe('/dashboard?period=week');
    expect(element.querySelector('.period[aria-pressed="true"]')?.getAttribute('data-period')).toBe('Week');
  });

  it('keeps the tab when the period changes, and the period when the tab changes', async () => {
    const harness = await open('/dashboard/operations');
    shell(harness).querySelector<HTMLButtonElement>('.period[data-period="Day"]')?.click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/dashboard/operations?period=day');

    harness.detectChanges();
    shell(harness).querySelector<HTMLAnchorElement>('nav.tabs a[href^="/dashboard/agents"]')?.click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/dashboard/agents?period=day');
  });
});
