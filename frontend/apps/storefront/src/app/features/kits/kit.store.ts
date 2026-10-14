import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { form, pattern, required } from '@angular/forms/signals';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import {
  patchState,
  signalMethod,
  signalStore,
  withComputed,
  withHooks,
  withLinkedState,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { KitTemplate, KitView, Money, PriceQuote } from '@tendero/shared-api';
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

/** What the address asks for: the budget with a point for its cents, and the requirements by facet code. */
interface KitAsk {
  readonly budget: string;
  readonly needs: readonly string[];
}

/** A budget as typed, as money; null when it is not one, or is nothing. */
function moneyOf(typed: string): Money | null {
  if (!Budget.test(typed)) return null;
  const amount = minorUnits(Number(typed.trim().replace(',', '.')), ShopCurrency);
  return amount > 0 ? { amount, currency: ShopCurrency } : null;
}

function askFrom(params: ParamMap): KitAsk {
  const budget = (params.get('budget') ?? '').trim();
  const needs = (params.get('needs') ?? '')
    .split(',')
    .map((need) => need.trim())
    .filter((need) => need.length > 0);
  return { budget: moneyOf(budget) ? budget : '', needs: [...new Set(needs)] };
}

/** The requirements asked that this kit can hold a product to: an address may name any. */
const heldTo = (template: KitTemplate | null, needs: readonly string[]) =>
  needs.filter((need) => template?.requirements.some((option) => option.facet === need) ?? false);

/** One question to the solver, as a key: the same key is the same kit. */
const keyOf = (template: string, budget: Money, requirements: readonly string[]) =>
  `${template}|${budget.amount}|${requirements.join(',')}`;

interface KitState {
  readonly kit: KitView | null;
  readonly building: boolean;
  readonly failed: boolean;
  /** What the kit comes to at the basket, promotions applied: the quote of exactly its lines. */
  readonly atTheBasket: PriceQuote | null;
  readonly adding: boolean;
  /** The cart's own words when it refused a line. */
  readonly cartProblem: string | null;
  /** The question last put to the solver, so the address the page writes is not asked again. */
  readonly _lastAsked: string | null;
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
 *
 * **What was asked lives in the address** (`?budget=59.90&needs=INDUCTION`),
 * as a listing's filters do. Switching language is a navigation to another
 * page — `/es/kits/…` to `/en/kits/…` — and a kit held only in this store came
 * back empty; from the address the new page builds it again, in its own
 * language. So does a reload, the back button and a link somebody was sent.
 * Only a kit somebody asked for with *Build* counts as `kit_built`: one the
 * page rebuilt from its address was asked for already.
 */
export const KitStore = signalStore(
  withState<KitState>(() => ({
    kit: null,
    building: false,
    failed: false,
    atTheBasket: null,
    adding: false,
    cartProblem: null,
    _lastAsked: null,
  })),
  withProps(() => {
    const route = inject(ActivatedRoute);
    const asked = toSignal(route.queryParamMap.pipe(map(askFrom)), {
      initialValue: askFrom(route.snapshot.queryParamMap),
    });
    // The field shows the address's budget, unless what is typed is that same
    // amount already: "59,90" is not rewritten "59.90" under the shopper.
    const draft = linkedSignal<KitAsk, { budget: string }>({
      source: asked,
      computation: (ask, previous) =>
        previous && moneyOf(previous.value.budget)?.amount === moneyOf(ask.budget)?.amount
          ? previous.value
          : { budget: ask.budget },
    });
    const budgetForm = form(
      draft,
      (kit) => {
        required(kit.budget);
        pattern(kit.budget, Budget);
      },
      { name: 'kit' },
    );
    return { _route: route, _asked: asked, draft, budgetForm };
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
  withLinkedState(({ _asked, template }) => ({
    /** The requirements switched on, by facet code: the address's, those this kit can hold a product to. */
    required: (): readonly string[] => heldTo(template(), _asked().needs),
  })),
  withMethods((store) => {
    /** Asks the shop for the kit, and what it comes to at the basket. */
    const ask = async (template: string, budget: Money, requirements: readonly string[], counted: boolean) => {
      patchState(store, {
        building: true,
        failed: false,
        cartProblem: null,
        _lastAsked: keyOf(template, budget, requirements),
      });
      try {
        const kit = await firstValueFrom(
          store._kits.build(template, { budget, requirements: [...requirements] }, store._culture.active()),
        );
        const lines = kitLines(kit.slots);
        patchState(store, { kit, atTheBasket: null });

        if (counted)
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
    };

    return {
      require(facet: string, on: boolean): void {
        const others = store.required().filter((each) => each !== facet);
        patchState(store, { required: on ? [...others, facet] : others });
      },

      /**
       * *Build*: asks the shop for the kit and writes what was asked into the
       * address. A budget that is not one asks for nothing: the field says what
       * is wrong with it.
       */
      async build(): Promise<void> {
        const template = store.template();
        const typed = store.draft().budget;
        const budget = moneyOf(typed);
        if (!template || !budget || store.building()) return;

        const requirements = [...store.required()];
        const asking = ask(template.code, budget, requirements, true);
        void store._router.navigate([], {
          relativeTo: store._route,
          queryParams: { budget: typed.trim().replace(',', '.'), needs: requirements.join(',') || null },
        });
        await asking;
      },

      /** The kit the address asks for, unless it is the one this page has just asked for. */
      _follow: signalMethod<{ template: string; budget: Money; requirements: readonly string[] } | null>((asked) => {
        if (!asked || keyOf(asked.template, asked.budget, asked.requirements) === store._lastAsked()) return;
        void ask(asked.template, asked.budget, asked.requirements, false);
      }),

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
    };
  }),
  withHooks({
    onInit(store) {
      store._follow(
        computed(() => {
          const template = store.template();
          const budget = moneyOf(store._asked().budget);
          if (!template || !budget) return null;
          return { template: template.code, budget, requirements: heldTo(template, store._asked().needs) };
        }),
      );
    },
  }),
);
