import { computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { form, pattern, required } from '@angular/forms/signals';
import { ActivatedRoute, Router } from '@angular/router';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { KitTemplate, KitView, PriceQuote } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { minorUnits, ShopCurrency } from '@tendero/shared-util';
import { firstValueFrom, map } from 'rxjs';
import { CartStore } from '../../cart/cart.store';
import { CartService } from '../../data-access/cart.service';
import { KitService } from '../../data-access/kit.service';
import { ShopLinks } from '../../shop-links';
import { budgetBand, kitLines } from './kit-sentences';

/** A budget as a person types it: digits, and cents after a comma or a point. */
const Budget = /^\s*\d{1,6}([.,]\d{1,2})?\s*$/;

interface KitState {
  /** The requirements switched on, by facet code. */
  readonly required: readonly string[];
  readonly kit: KitView | null;
  readonly building: boolean;
  readonly failed: boolean;
  /** What the kit comes to at the basket, promotions applied: the quote of exactly its lines. */
  readonly atTheBasket: PriceQuote | null;
  readonly adding: boolean;
  /** The cart's own words when it refused a line. */
  readonly cartProblem: string | null;
}

export type KitPageState = 'loading' | 'notFound' | 'failed' | 'ready';

/**
 * One kit's page (phase 13c, K-5; ADR 0047): the budget and the requirements
 * a shopper gives, the kit the shop built from them, and the kit going to the
 * shop's one cart.
 *
 * The shop chooses; the page shows. Nothing here ranks, adds or compares
 * prices — the solver did, and its answer carries the total, what is left and
 * why each slot is what it is. The one thing the page asks for besides is the
 * quote of the kit's lines, through the call the cart already makes, because
 * a kit is held to shown prices and a promotion is shown after (the spec,
 * decision 5).
 */
export const KitStore = signalStore(
  withState<KitState>(() => ({
    required: [],
    kit: null,
    building: false,
    failed: false,
    atTheBasket: null,
    adding: false,
    cartProblem: null,
  })),
  withProps(() => {
    const draft = signal({ budget: '' });
    const budgetForm = form(
      draft,
      (kit) => {
        required(kit.budget);
        pattern(kit.budget, Budget);
      },
      { name: 'kit' },
    );
    return { draft, budgetForm };
  }),
  withProps(() => {
    const kits = inject(KitService);
    const culture = inject(CultureStore);
    return {
      _kits: kits,
      _culture: culture,
      _cart: inject(CartStore),
      _quotes: inject(CartService),
      _router: inject(Router),
      _links: inject(ShopLinks),
      _telemetry: inject(Telemetry),
      code: toSignal(inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('code') ?? '')), {
        initialValue: '',
      }),
      _templates: extendResource(
        rxResource({
          params: () => ({ culture: culture.active() }),
          stream: ({ params }) => kits.list(params.culture),
        }),
      ),
    };
  }),
  withComputed(({ code, _templates, kit }) => {
    const template = computed<KitTemplate | null>(
      () => _templates.value()?.find((each) => each.code.toUpperCase() === code().toUpperCase()) ?? null,
    );
    return {
      template,
      state: computed<KitPageState>(() => {
        if (_templates.status() === 'error') return 'failed';
        if (!_templates.hasValue()) return 'loading';
        return template() ? 'ready' : 'notFound';
      }),
      /** What adding the kit would put in the cart. */
      lines: computed(() => kitLines(kit()?.slots ?? [])),
    };
  }),
  withMethods((store) => ({
    require(facet: string, on: boolean): void {
      const others = store.required().filter((each) => each !== facet);
      patchState(store, { required: on ? [...others, facet] : others });
    },

    /** Asks the shop for the kit. A budget that is not one asks for nothing: the field says what is wrong with it. */
    async build(): Promise<void> {
      const template = store.template();
      const typed = store.draft().budget;
      if (!template || !Budget.test(typed) || store.building()) return;

      const budget = {
        amount: minorUnits(Number(typed.trim().replace(',', '.')), ShopCurrency),
        currency: ShopCurrency,
      };
      if (budget.amount <= 0) return;

      patchState(store, { building: true, failed: false, cartProblem: null });
      try {
        const kit = await firstValueFrom(
          store._kits.build(template.code, { budget, requirements: [...store.required()] }, store._culture.active()),
        );
        const lines = kitLines(kit.slots);
        patchState(store, { kit, atTheBasket: null });

        store._telemetry.trackEvent('kit_built', {
          template: kit.template,
          budget: budgetBand(kit.budget),
          filled: lines.length,
          empty: kit.slots.length - lines.length,
          binding: kit.binding.toLowerCase() as 'none' | 'budget' | 'candidates',
          source: 'form',
        });

        // What it comes to with the shop's promotions: a second answer, and
        // the kit is whole without it — a quote that fails leaves it unsaid.
        if (lines.length > 0) {
          const quote = await firstValueFrom(store._quotes.quote(lines, null, store._culture.active())).catch(
            () => null,
          );
          patchState(store, { atTheBasket: quote });
        }
      } catch {
        patchState(store, { kit: null, atTheBasket: null, failed: true });
      } finally {
        patchState(store, { building: false });
      }
    },

    /**
     * One of each product the kit chose, through the cart every other page
     * uses — so what the cart refuses, it refuses here in its own words, and
     * the lines before it stay in.
     */
    async addToCart(): Promise<void> {
      const kit = store.kit();
      const lines = store.lines();
      if (!kit || lines.length === 0 || store.adding()) return;

      patchState(store, { adding: true, cartProblem: null });
      try {
        for (const line of lines) {
          await store._cart.add(line.sku, line.quantity);
          const refused = store._cart.failed();
          if (refused) {
            patchState(store, { cartProblem: refused });
            return;
          }
        }

        store._telemetry.trackEvent('kit_added_to_cart', { template: kit.template, lines: lines.length });
        await store._router.navigate(store._links.cart());
      } finally {
        patchState(store, { adding: false });
      }
    },
  })),
);
