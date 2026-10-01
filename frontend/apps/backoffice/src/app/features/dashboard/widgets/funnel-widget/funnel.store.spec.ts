import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CheckoutFunnel } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { FunnelStore } from './funnel.store';

const funnel = (overrides: Partial<CheckoutFunnel>): CheckoutFunnel => ({
  period: 'Week',
  cartsStarted: 20,
  checkedOut: 5,
  abandoned: 10,
  stillOpen: 5,
  conversion: 0.25,
  newCustomers: 3,
  returningCustomers: 2,
  ...overrides,
});

/** The checkout funnel (R-9, D-7): each step as a share of the carts started, and nothing drawn wide when none were. */
describe('the funnel store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        DashboardSources,
        DashboardRefresh,
        FunnelStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function asked() {
    const store = TestBed.inject(FunnelStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    return store;
  }

  async function answered(body: CheckoutFunnel) {
    const store = asked();
    http.expectOne('http://api/api/dashboard/funnel?period=Week').flush(body);
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('measures each step against the carts started', async () => {
    const store = await answered(funnel({}));

    expect(store.steps().map((step) => [step.key, step.count, step.share])).toEqual([
      ['started', 20, 1],
      ['checkedOut', 5, 0.25],
      ['abandoned', 10, 0.5],
      ['stillOpen', 5, 0.25],
    ]);
  });

  it('draws no bar wide when no cart was started', async () => {
    const store = await answered(funnel({ cartsStarted: 0, checkedOut: 0, abandoned: 0, stillOpen: 0 }));

    expect(store.steps().map((step) => step.share)).toEqual([0, 0, 0, 0]);
  });

  it('has no steps until the funnel answers', () => {
    const store = asked();

    expect(store.steps()).toEqual([]);
  });
});
