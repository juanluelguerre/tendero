import { inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { patchState, signalMethod, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import type { SetImageAltTextRequest } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { CatalogService } from '../../data-access/catalog.service';
import { extendResource } from '@ngrx/signals/resource';

/**
 * A product as publishing it would publish it, as state (ADR 0047): its own
 * read — every culture as stored, the images, the attributes, the price — by the
 * code its component is given. Nothing is asked until there is one.
 */
export const ProductReviewStore = signalStore(
  withState({
    code: null as string | null,
    /** The picture whose alt text is being written. */
    saving: null as string | null,
    /** The picture whose words were refused: they stay in their boxes for the person to correct. */
    notSaved: null as string | null,
  }),
  withProps((store) => {
    const catalog = inject(CatalogService);
    const culture = inject(CultureStore);
    return {
      _catalog: catalog,
      review: extendResource(
        rxResource({
          params: () => {
            const code = store.code();
            return code ? { code, culture: culture.active() } : undefined;
          },
          stream: ({ params }) => catalog.review(params.code, params.culture),
        }),
      ),
    };
  }),
  withMethods((store) => ({
    /** Pass the component's input signal: the store follows it for as long as the component lives. */
    follow: signalMethod<string>((code) => patchState(store, { code })),
    /** Writes what a person typed for one picture, in both languages, then reads the product again. */
    describe(productId: string, imageId: string, alt: SetImageAltTextRequest): void {
      patchState(store, { saving: imageId, notSaved: null });
      store._catalog.describeImage(productId, imageId, alt).subscribe({
        next: () => {
          patchState(store, { saving: null });
          store.review.reload();
        },
        error: () => patchState(store, { saving: null, notSaved: imageId }),
      });
    },
  })),
);
