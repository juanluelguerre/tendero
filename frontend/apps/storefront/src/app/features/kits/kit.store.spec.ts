import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Params, Router } from '@angular/router';
import type { BuildKitRequest, KitTemplate, KitView, PriceQuote } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
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
  /** Where the page went: the cart's page, or its own address with what was asked in it. */
  let went: { commands: unknown[]; queryParams?: Params }[];
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let building: () => ReturnType<KitService['build']>;
  /** The reader's language, which the switch in the header changes under a page that stays. */
  let culture: ReturnType<typeof signal<string>>;
  /** The languages each kit was asked in. */
  let askedIn: string[];
  let listing: () => ReturnType<KitService['list']>;

  beforeEach(() => {
    culture = signal('es');
    askedIn = [];
    listing = () => of([kitchen]);
    asked = [];
    quotedFor = [];
    added = [];
    refusedSku = null;
    events = [];
    went = [];
    query = new BehaviorSubject(convertToParamMap({}));
    building = () => of(built);
  });

  async function opened(code = 'KITCHEN_STARTER', address: Params = {}, arrivedWith: Record<string, unknown> = {}) {
    query.next(convertToParamMap(address));
    const failed = signal<string | null>(null);

    TestBed.configureTestingModule({
      providers: [
        KitStore,
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ code })),
            queryParamMap: query,
            snapshot: {
              get queryParamMap() {
                return query.value;
              },
            },
          },
        },
        { provide: CultureStore, useValue: { active: culture } },
        {
          // As the real one does: going to its own address with new parameters changes what the route reads.
          provide: Router,
          useValue: {
            currentNavigation: () => ({ extras: { state: arrivedWith } }),
            navigate: async (commands: unknown[], extras?: { queryParams?: Params }) => {
              went.push({ commands, queryParams: extras?.queryParams });
              if (extras?.queryParams) query.next(convertToParamMap(extras.queryParams));
            },
          },
        },
        { provide: Telemetry, useValue: { trackEvent: (...event: unknown[]) => void events.push(event) } },
        {
          provide: KitService,
          useValue: {
            list: () => listing(),
            build: (template: string, request: BuildKitRequest, language: string) => (
              asked.push({ template, request }),
              askedIn.push(language),
              building()
            ),
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
    expect(went.at(-1)?.commands).toEqual(['/', 'es', 'cart']);
  });

  it('stays on the page with the cart’s own words when the cart refuses a line', async () => {
    refusedSku = 'PAN-26';
    const store = await opened();
    store.draft.set({ budget: '210' });
    await store.build();

    await store.addToCart();

    expect([
      store.cartProblem(),
      went.some((each) => each.queryParams === undefined),
      events.some(([name]) => name === 'kit_added_to_cart'),
    ]).toEqual(['cart.failed', false, false]);
  });

  /**
   * What was asked lives in the address, as a listing's filters do: switching
   * language is a navigation to another page (/es → /en), and a kit held only
   * in the page's memory came back empty (the author, 2026-10-14).
   */
  /**
   * Switching language keeps the page (one route for every language): the kit
   * on it is asked again in the new language, its names being the shop's in
   * that language, and the page does not go back to loading while the kits are
   * read again (the author, 2026-10-14).
   */
  describe('the language switched under the page', () => {
    it('asks for the kit again in the new language, and counts nothing', async () => {
      const store = await opened();
      store.draft.set({ budget: '210' });
      await store.build();

      culture.set('en');
      await TestBed.inject(ApplicationRef).whenStable();

      expect(askedIn).toEqual(['es', 'en']);
      expect(events.filter(([name]) => name === 'kit_built')).toHaveLength(1);
    });

    it('stays ready while the kits are read again in the new language', async () => {
      const store = await opened();
      const reading = new Subject<KitTemplate[]>();
      listing = () => reading;

      // Ticked, not awaited: the read is held open on purpose, so the app is never stable.
      culture.set('en');
      TestBed.tick();

      expect([store.state(), store.template()?.code]).toEqual(['ready', 'KITCHEN_STARTER']);
      reading.next([{ ...kitchen, name: 'Kitchen kit' }]);
      reading.complete();
      await TestBed.inject(ApplicationRef).whenStable();
      expect(store.template()?.name).toBe('Kitchen kit');
    });
  });

  describe('the address', () => {
    it('keeps the budget and what was switched on once the kit is built', async () => {
      const store = await opened();
      store.draft.set({ budget: '59,90' });
      store.require('INDUCTION', true);

      await store.build();

      expect(went).toEqual([{ commands: [], queryParams: { budget: '59.90', needs: 'INDUCTION' } }]);
    });

    it('builds the kit it holds when the page opens, in the page’s language, and counts nothing', async () => {
      const store = await opened('KITCHEN_STARTER', { budget: '59.90', needs: 'INDUCTION' });

      expect(asked).toEqual([
        { template: 'KITCHEN_STARTER', request: { budget: eur(5990), requirements: ['INDUCTION'] } },
      ]);
      expect([store.kit(), store.draft().budget, store.required()]).toEqual([built, '59.90', ['INDUCTION']]);
      expect(events).toEqual([]);
    });

    it('asks for nothing with no budget, or one that is not one, and drops what this kit cannot be held to', async () => {
      await opened('KITCHEN_STARTER', { needs: 'INDUCTION' });
      TestBed.resetTestingModule();
      await opened('KITCHEN_STARTER', { budget: 'mucho' });
      TestBed.resetTestingModule();
      const store = await opened('KITCHEN_STARTER', { budget: '210', needs: 'INDUCTION,GOLD' });

      expect(asked.map((each) => each.request)).toEqual([{ budget: eur(21000), requirements: ['INDUCTION'] }]);
      expect(store.required()).toEqual(['INDUCTION']);
    });

    /**
     * The one rebuild that counts: a page the box sent here (K-6) builds what
     * the sentence asked for, and that is the sentence's kit. Said by the
     * navigation, never the address, so a reload of it counts nothing.
     */
    it('counts the kit a sentence asked for, once, as the sentence’s', async () => {
      const store = await opened('KITCHEN_STARTER', { budget: '200', needs: 'INDUCTION' }, { kitSource: 'sentence' });
      store.draft.set({ budget: '210' });
      await store.build();

      expect(events.map(([name, attributes]) => [name, (attributes as { source: string }).source])).toEqual([
        ['kit_built', 'sentence'],
        ['kit_built', 'form'],
      ]);
    });

    it('does not ask again for the kit the page has just built and written there', async () => {
      const store = await opened();
      store.draft.set({ budget: '210' });

      await store.build();
      await TestBed.inject(ApplicationRef).whenStable();

      expect(asked).toHaveLength(1);
      expect(events.filter(([name]) => name === 'kit_built')).toHaveLength(1);
    });
  });
});
