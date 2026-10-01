import { computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { signalStoreFeature, withComputed, withMethods, withProps } from '@ngrx/signals';

/** How many rows a list of work shows at once, in every list. */
export const WorklistPageSize = 20;

/** What a list's own query parameters say: `status`, `order`, `page: null` to drop one. */
export type WorklistParams = Record<string, string | number | null>;

/**
 * What every list of work keeps in its URL (ADR 0047): its filter, the open
 * row and the page, so a dashboard figure or the palette can open a list
 * exactly there, page three can be shared, and the back button undoes a step.
 *
 * Four stores read the query and paged by hand, the same few lines each — and
 * one of them in a slightly different way (the review of 2026-10-01, N5). A
 * store adds this, takes its own filter from `_params()` and the page from
 * `page()`, and changes them with `_navigate()` and `go()`. A page past the
 * end is the pager's to keep a list out of: it shows the last and moves from it.
 */
export function withWorklistUrl() {
  return signalStoreFeature(
    withProps(() => {
      const route = inject(ActivatedRoute);
      return {
        _route: route,
        _router: inject(Router),
        _params: toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap }),
        pageSize: WorklistPageSize,
      };
    }),
    withComputed(({ _params }) => ({
      /** The page in the URL; anything that is not a page number is the first. */
      page: computed(() => Math.max(1, Number(_params().get('page') ?? 1) || 1)),
    })),
    withMethods(({ _router }) => {
      /** A change to the list's place: merged into the query, so it lands in the history. */
      const navigate = (queryParams: WorklistParams, replaceUrl = false): void =>
        void _router.navigate([], { queryParams, queryParamsHandling: 'merge', replaceUrl });

      return {
        _navigate: navigate,
        /** The first page says nothing in the URL. */
        go(page: number): void {
          navigate({ page: page <= 1 ? null : page });
        },
      };
    }),
  );
}
