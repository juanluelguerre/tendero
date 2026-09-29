import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DashboardPage } from './dashboard-page';
import { DashboardRefresh } from './dashboard-refresh';
import { dashboardChildren } from './dashboard-routes';
import { DASHBOARD_WIDGETS, type WidgetDefinition, type WidgetKey } from './dashboard-widgets';
import { FullScreen } from './full-screen';
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
  const active = signal(false);
  const fake = {
    supported: true,
    active: active.asReadonly(),
    enter: vi.fn(async () => active.set(true)),
    leave: vi.fn(async () => active.set(false)),
  };

  afterEach(() => {
    active.set(false);
    vi.useRealTimers();
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        provideRouter([{ path: 'dashboard', component: DashboardPage, children: dashboardChildren }]),
        { provide: FullScreen, useValue: fake },
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

  it('puts its own section on the screen, and shows the tab, the period, the time and the way out there', async () => {
    const harness = await open('/dashboard/operations?period=day');
    const element = shell(harness);
    element.querySelector<HTMLButtonElement>('.full-screen-enter')?.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    const head = element.querySelector('.wall-head');

    expect(fake.enter).toHaveBeenCalledWith(element.querySelector('section.dashboard'));
    expect(head?.textContent).toContain('Operations');
    expect(head?.textContent).toContain('Last 24 h');
    expect(head?.textContent).toContain('refreshes every minute');
    expect(head?.querySelector('.full-screen-leave')).not.toBeNull();
    expect(element.querySelector('nav.tabs')).toBeNull();
  });

  it('refreshes every minute on the wall, and only there', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    const harness = await open('/dashboard');
    const refresh = harness.fixture.debugElement.query((node) => node.componentInstance instanceof DashboardPage).injector.get(DashboardRefresh);
    const before = refresh.at();
    vi.advanceTimersByTime(120_000);
    expect(refresh.at()).toBe(before);

    active.set(true);
    harness.detectChanges();
    vi.advanceTimersByTime(60_000);
    expect(refresh.at()).toBeGreaterThan(before);
  });

  /** Review focus 3: leaving by Esc is the browser's event; the page comes back and the tick stops. */
  it('leaves the wall when the browser does', async () => {
    const harness = await open('/dashboard');
    active.set(true);
    harness.detectChanges();
    active.set(false);
    harness.detectChanges();

    expect(shell(harness).querySelector('.wall-head')).toBeNull();
    expect(shell(harness).querySelector('nav.tabs')).not.toBeNull();
  });
});
