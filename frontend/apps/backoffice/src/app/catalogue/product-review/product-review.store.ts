import { inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { patchState, signalMethod, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { CatalogService } from '../../data-access/catalog.service';

/**
 * A product as publishing it would publish it, as state (ADR 0047): its own
 * read — every culture as stored, the images, the attributes, the price — by the
 * code its component is given. Nothing is asked until there is one.
 */
export const ProductReviewStore = signalStore(
  withState<{ code: string | null }>({ code: null }),
  withProps((store) => {
    const catalog = inject(CatalogService);
    const culture = inject(CultureStore);
    return {
      review: rxResource({
        params: () => {
          const code = store.code();
          return code ? { code, culture: culture.active() } : undefined;
        },
        stream: ({ params }) => catalog.review(params.code, params.culture),
      }),
    };
  }),
  withMethods((store) => ({
    /** Pass the component's input signal: the store follows it for as long as the component lives. */
    follow: signalMethod<string>((code) => patchState(store, { code })),
  })),
);
