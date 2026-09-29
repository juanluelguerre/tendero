import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { signalStore } from '@ngrx/signals';
import { describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../data-access/ordering.service';
import { withDashboardPeriod } from './with-dashboard-period';

const Store = signalStore(withDashboardPeriod());

describe('a store on the dashboard period', () => {
  /** No period, no question: a widget on the day must not first ask for the week and throw it away. */
  it('has no period until its widget gives it one', () => {
    TestBed.configureTestingModule({ providers: [Store] });

    expect([TestBed.inject(Store).period(), TestBed.inject(Store).periodParams()]).toEqual([null, undefined]);
  });

  /** The widget's input is the source: the store follows it, not a copy made once. */
  it('follows the period its widget is given', () => {
    TestBed.configureTestingModule({ providers: [Store] });
    const store = TestBed.inject(Store);
    const period = signal<DashboardPeriod>('Day');
    TestBed.runInInjectionContext(() => store.followPeriod(period));
    TestBed.tick();
    const first = store.period();
    period.set('Month');
    TestBed.tick();

    expect([first, store.period()]).toEqual(['Day', 'Month']);
    expect(store.periodParams()).toEqual({ period: 'Month' });
  });
});
