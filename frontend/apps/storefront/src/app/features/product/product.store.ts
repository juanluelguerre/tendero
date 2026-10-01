import { computed, inject } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import {
  patchState,
  signalStore,
  withComputed,
  withLinkedState,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import type { ProductDetail, VariantView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { map } from 'rxjs';
import { CartStore } from '../../cart/cart.store';
import { statusOf } from '../../failure-status';
import { ProductService } from '../../data-access/product.service';
import { SpecsService } from '../../data-access/specs.service';
import { specLines, specStatements } from '../../specs/spec-lines';
import {
  availabilityOf,
  initialSelection,
  select,
  sellable,
  variantFor,
  type OptionAvailability,
  type Selection,
} from './variant-picker';
import { extendResource } from '@ngrx/signals/resource';

export type ProductPageState =
  { status: 'loading' } | { status: 'done'; product: ProductDetail } | { status: 'notFound' } | { status: 'failed' };

interface ProductState {
  readonly adding: boolean;
  /**
   * What the last Add to cart did, for the notice beside the button (the
   * redesign's R-5.3). Only the header's count used to change, away from where
   * the shopper was looking, and a refusal changed nothing at all: the baseline
   * walkthrough's P1 asked for a word. The SKU and not a flag, so choosing
   * another variant takes the notice away rather than claiming it was added.
   */
  readonly added: { readonly sku: string; readonly ok: boolean } | null;
}

/**
 * The product page's state (ADR 0047): the product the URL names, its specs
 * with their sources, the variant chosen, the gallery and what adding to the
 * cart did. The page presents it — prices in the reader's culture, the tab's
 * title, the canonical address — and this reads and derives.
 */
export const ProductStore = signalStore(
  withState<ProductState>({ adding: false, added: null }),
  withProps(() => {
    const route = inject(ActivatedRoute);
    const products = inject(ProductService);
    const specs = inject(SpecsService);
    const culture = inject(CultureStore);
    const code = toSignal(route.paramMap.pipe(map((params) => params.get('code') ?? '')), { initialValue: '' });
    const asked = () => {
      const current = code();
      return current ? { code: current, culture: culture.active() } : undefined;
    };
    return {
      _cart: inject(CartStore),
      _code: code,
      slugFromUrl: toSignal(route.paramMap.pipe(map((params) => params.get('slug') ?? '')), { initialValue: '' }),
      /**
       * The product, as a function of the page's only two inputs: the code in the
       * URL and the language.
       *
       * Reloading on a language change is not optional: the name, the
       * description, the attribute values and the variant labels are all
       * resolved by the server in the requested culture, so a switch that only
       * retranslated the chrome would leave Spanish product copy under English
       * labels. A resource CANCELS the request in flight when its inputs change,
       * so a slow answer in the old language never paints over the new one.
       */
      _detail: extendResource(
        rxResource({ params: asked, stream: ({ params }) => products.get(params.code, params.culture) }),
      ),
      /**
       * The same product's specs with their sources (P10-8), beside the product
       * and never in front of it: the page renders from the catalogue first, and
       * the chips arrive when Knowledge answers — or never, if it does not
       * (invariant 8).
       */
      _sheet: extendResource(
        rxResource({ params: asked, stream: ({ params }) => specs.get([params.code], params.culture) }),
      ),
    };
  }),
  withComputed(({ _code, _detail, _sheet }) => {
    const state = computed<ProductPageState>(() => {
      if (!_code()) return { status: 'notFound' };
      switch (_detail.status()) {
        case 'resolved':
        case 'local':
          return { status: 'done', product: _detail.value() as ProductDetail };
        case 'error':
          // 404: no such product; 400: no such code — a link copied short. Either
          // way the shopper is told it does not exist, never to try again.
          return { status: [400, 404].includes(statusOf(_detail.error()) ?? 0) ? 'notFound' : 'failed' };
        default:
          return { status: 'loading' };
      }
    });
    const product = computed(() => {
      const current = state();
      return current.status === 'done' ? current.product : null;
    });
    /** A failed sheet is no sheet: the lines fall back to the catalogue's attributes. */
    const loadedSheet = computed(() => (_sheet.status() === 'resolved' ? _sheet.value() : undefined));
    /** The variants a shopper can choose between — discontinued ones are not on sale. */
    const variants = computed(() => {
      const current = product();
      return current ? sellable(current) : [];
    });
    return {
      state,
      product,
      variants,
      specLines: computed(() => {
        const current = product();
        return current ? specLines(current.attributes, loadedSheet()) : [];
      }),
      statements: computed(() => specStatements(loadedSheet())),
    };
  }),
  // What the shopper picks, reset with each product: each answer opens on the
  // variant it can sell, with the cover in front. State linked to the product,
  // where an effect used to patch it after every answer.
  withLinkedState(({ product }) => ({
    /** Which option is chosen on each axis. */
    selection: (): Selection => {
      const current = product();
      return current ? initialSelection(current) : {};
    },
    /** Which image the gallery is showing, by id. Null means the cover. */
    shown: (): string | null => {
      product();
      return null;
    },
  })),
  withComputed(({ variants, selection, shown, product, added }) => {
    const chosen = computed(() => variantFor(variants(), selection()));
    return {
      /** The one the picker is currently on, or null when the combination is not sold. */
      chosen,
      /** The gallery's images, cover first, with the chosen variant's promoted. */
      gallery: computed(() => {
        const current = product();
        if (!current) return [];
        const promotedId = shown();
        const images = [...current.images];
        if (!promotedId) return images;
        const promoted = images.filter((image) => image.imageId === promotedId);
        return promoted.length > 0 ? [...promoted, ...images.filter((image) => image.imageId !== promotedId)] : images;
      }),
      /** The notice, if it is about the variant on screen. */
      notice: computed(() => {
        const last = added();
        return last && last.sku === chosen()?.sku ? last : null;
      }),
      /** Whether the shelf is thin enough to be worth saying. The API only sends a
       *  number when it is, so the page never decides the threshold. */
      remaining: computed(() => chosen()?.remaining ?? null),
    };
  }),
  withMethods((store) => ({
    availability(axis: string, option: string): OptionAvailability {
      return availabilityOf(store.variants(), store.selection(), axis, option);
    },
    isChosen(axis: string, option: string): boolean {
      return store.selection()[axis] === option;
    },
    choose(axis: string, option: string): void {
      patchState(store, { selection: select(store.variants(), store.selection(), axis, option) });
      // A colour has its own photo; a size does not. Moving the gallery back to
      // the variant's image is what makes the picker feel connected to it.
      patchState(store, { shown: store.chosen()?.imageId ?? null });
    },
    /** A thumbnail pressed: its image goes in front. */
    show(imageId: string): void {
      patchState(store, { shown: imageId });
    },
    /**
     * Adds the chosen variant. The button is disabled without one, so a shopper
     * cannot buy a combination the shop does not sell — which is the whole
     * reason the picker distinguishes "not sold" from "sold out".
     */
    async addToCart(): Promise<void> {
      const variant = store.chosen();
      if (!variant || !variant.inStock) return;

      patchState(store, { adding: true, added: null });
      try {
        await store._cart.add(variant.sku);
      } finally {
        patchState(store, { adding: false });
      }
      // The cart keeps a refusal rather than throwing it; its absence is success.
      patchState(store, { added: { sku: variant.sku, ok: store._cart.failed() === null } });
    },
    variantOf(sku: string): VariantView | undefined {
      return store.variants().find((variant) => variant.sku === sku);
    },
  })),
);
