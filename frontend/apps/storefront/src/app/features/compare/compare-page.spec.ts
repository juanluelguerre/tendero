import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Telemetry } from '@tendero/shared-telemetry';
import { beforeEach, describe, expect, it } from 'vitest';
import { ShopLinks } from '../../shop-links';
import { ComparePage } from './compare-page';
import { ComparisonStore } from './comparison.store';

type ComparisonState = ReturnType<InstanceType<typeof ComparisonStore>['state']>;

/**
 * What the comparison page reports: `products_compared` once per set of
 * products, however often the same set is read again (a language switch).
 * The page's logic only — its template is the browser suite's.
 */
describe('the comparison page', () => {
  let events: [string, unknown][];
  const state = signal<ComparisonState>({ status: 'loading' } as ComparisonState);

  const done = (...codes: string[]) =>
    ({
      status: 'done',
      sheet: { products: codes.map((code) => ({ code })), statements: [] },
    }) as unknown as ComparisonState;

  beforeEach(() => {
    events = [];
    state.set({ status: 'loading' } as ComparisonState);
    TestBed.configureTestingModule({
      providers: [
        {
          provide: Telemetry,
          useValue: { trackEvent: (name: string, attributes: unknown) => events.push([name, attributes]) },
        },
        { provide: ShopLinks, useValue: {} },
      ],
    });
    TestBed.overrideComponent(ComparePage, {
      set: { template: '', imports: [], providers: [{ provide: ComparisonStore, useValue: { state } }] },
    });
  });

  it('reports a comparison once per set of products, not once per answer', () => {
    const fixture = TestBed.createComponent(ComparePage);
    TestBed.tick();
    state.set(done('A', 'B'));
    TestBed.tick();
    state.set(done('A', 'B'));
    TestBed.tick();
    state.set(done('A', 'B', 'C'));
    TestBed.tick();
    fixture.destroy();

    expect(events).toEqual([
      ['products_compared', { products: 2 }],
      ['products_compared', { products: 3 }],
    ]);
  });
});
