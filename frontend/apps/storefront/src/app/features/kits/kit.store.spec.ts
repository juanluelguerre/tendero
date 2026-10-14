import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import type { BuildKitRequest, KitTemplate, KitView, PriceQuote } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CartStore } from '../../cart/cart.store';
import { CartService } from '../../data-access/cart.service';
import { KitService } from '../../data-access/kit.service';
import { KitStore } from './kit.store';

const eur = (amount: number) => ({ amount, currency: 'EUR' });

const kitchen: KitTemplate = {
  code: 'KITCHEN_STARTER',
  name: 'Kit de cocina',
  slots: [
    { code: 'PAN', name: 'Sartén', required: true },
    { code: 'BOARD', name: 'Tabla', required: false },
  ],
  requirements: [{ facet: 'INDUCTION', label: 'Inducción' }],
};

const built = {
  template: 'KITCHEN_STARTER',
  budget: eur(21000),
  total: eur(4990),
  left: eur(16010),
  binding: 'Candidates',
  slots: [
    {
      code: 'PAN',
      name: 'Sartén',
      required: true,
      requirements: [],
      product: { matchedSku: 'PAN-26', name: 'Sartén' },
      gap: null,
      better: null,
    },
    {
      code: 'BOARD',
      name: 'Tabla',
      required: false,
      requirements: [],
      product: null,
      gap: { reason: 'NoCandidate', cheapest: null, short: null },
      better: null,
    },
  ],
} as unknown as KitView;

/** One kit's page (ADR 0047): the budget asked, the kit the shop built, and the kit going to the shop's one cart. */
describe('the kit store', () => {
  let asked: { template: string; request: BuildKitRequest }[];
  let quotedFor: { sku: string; quantity: number }[][];
  let added: string[];
  let refusedSku: string | null;
  let events: unknown[][];
  let went: unknown[];
  let building: () => ReturnType<KitService['build']>;

  beforeEach(() => {
    asked = [];
    quotedFor = [];
    added = [];
    refusedSku = null;
    events = [];
    went = [];
    building = () => of(built);
  });

  async function opened(code = 'KITCHEN_STARTER') {
    const failed = signal<string | null>(null);

    TestBed.configureTestingModule({
      providers: [
        KitStore,
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ code })) } },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: Router, useValue: { navigate: async (commands: unknown[]) => void went.push(commands) } },
        { provide: Telemetry, useValue: { trackEvent: (...event: unknown[]) => void events.push(event) } },
        {
          provide: KitService,
          useValue: {
            list: () => of([kitchen]),
            build: (template: string, request: BuildKitRequest) => (asked.push({ template, request }), building()),
          },
        },
        {
          provide: CartService,
          useValue: {
            quote: (lines: { sku: string; quantity: number }[]) => (
              quotedFor.push(lines),
              of({ total: eur(4490) } as unknown as PriceQuote)
            ),
          },
        },
        {
          provide: CartStore,
          useValue: {
            failed,
            add: async (sku: string) => {
              failed.set(sku === refusedSku ? 'cart.failed' : null);
              if (sku !== refusedSku) added.push(sku);
            },
          },
        },
      ],
    });

    const store = TestBed.inject(KitStore);
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('finds the kit the address names, and says so when the shop has none by that name', async () => {
    expect((await opened()).state()).toBe('ready');
    TestBed.resetTestingModule();
    expect((await opened('NOPE')).state()).toBe('notFound');
  });

  it('asks for the kit with the budget as money and what was switched on, and for what it comes to at the basket', async () => {
    const store = await opened();
    store.draft.set({ budget: '210' });
    store.require('INDUCTION', true);

    await store.build();

    expect(asked).toEqual([
      { template: 'KITCHEN_STARTER', request: { budget: eur(21000), requirements: ['INDUCTION'] } },
    ]);
    expect(store.kit()).toBe(built);
    // The solver's slice does not reach Pricing: the page asks for the quote, of exactly what the kit holds.
    expect(quotedFor).toEqual([[{ sku: 'PAN-26', quantity: 1 }]]);
    expect(store.atTheBasket()?.total).toEqual(eur(4490));
    expect(events).toEqual([
      [
        'kit_built',
        { template: 'KITCHEN_STARTER', budget: '200_500', filled: 1, empty: 1, binding: 'candidates', source: 'form' },
      ],
    ]);
  });

  it('reads a budget as a person types it, and builds nothing from one that is not a number', async () => {
    const store = await opened();

    store.draft.set({ budget: '59,90' });
    await store.build();
    store.draft.set({ budget: 'mucho' });
    await store.build();
    store.draft.set({ budget: '' });
    await store.build();

    expect(asked.map((each) => each.request.budget)).toEqual([eur(5990)]);
  });

  it('says the kit could not be built, and keeps the form as it was', async () => {
    building = () => throwError(() => ({ status: 503 }));
    const store = await opened();
    store.draft.set({ budget: '210' });

    await store.build();

    expect([store.kit(), store.failed(), store.building()]).toEqual([null, true, false]);
    expect(events).toEqual([]);
  });

  it('puts one of each product in the cart, reports it and goes there', async () => {
    const store = await opened();
    store.draft.set({ budget: '210' });
    await store.build();

    await store.addToCart();

    expect(added).toEqual(['PAN-26']);
    expect(events.at(-1)).toEqual(['kit_added_to_cart', { template: 'KITCHEN_STARTER', lines: 1 }]);
    expect(went).toHaveLength(1);
  });

  it('stays on the page with the cart’s own words when the cart refuses a line', async () => {
    refusedSku = 'PAN-26';
    const store = await opened();
    store.draft.set({ budget: '210' });
    await store.build();

    await store.addToCart();

    expect([store.cartProblem(), went, events.some(([name]) => name === 'kit_added_to_cart')]).toEqual([
      'cart.failed',
      [],
      false,
    ]);
  });
});
