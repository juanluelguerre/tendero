import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoService, translateObjectSignal } from '@jsverse/transloco';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { ImportResult, ProductStatus, ProductSummary } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { debounceTime, distinctUntilChanged, type Observable } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { readStatus } from '../../read-status';
import { BackofficeUsage } from '../../usage';
import type { FilterOption } from '../../worklist/filter-chips/filter-chips';
import { keptCounts, statusChips } from '../../worklist/filter-chips/status-chips';
import { UndoStore } from '../../worklist/undo/undo.store';
import { valueOf } from '@tendero/shared-util';

/** Twenty rows a page, as every worklist (R-7.2). */
export const PageSize = 20;

/** The statuses a product can be in, in the chips' order: what is on sale first. */
const Statuses: readonly ProductStatus[] = ['Active', 'Draft', 'Archived'];

/**
 * The catalogue's shared state (ADR 0047): the status, the search, the page and
 * the open product in the URL; the products; and the changes a shopkeeper makes
 * — publish, withdraw with Undo, import the seed — which the list and the pane
 * both show. A change replaces its row with the status the server answered.
 */
export const CatalogueStore = signalStore(
  withState(() => ({
    /** What is typed, from the URL's `q` the first time. */
    text: inject(ActivatedRoute).snapshot.queryParamMap.get('q') ?? '',
    busy: null as string | null,
    /** Empty when the server refused without a sentence; null when nothing was refused. */
    refusal: null as string | null,
    importing: false,
    /**
     * What the last import did: "it worked" and "it created ninety-four products"
     * are different amounts of information, and the second is the answer.
     */
    imported: null as ImportResult | null,
  })),
  withProps((store) => {
    const route = inject(ActivatedRoute);
    return {
      _router: inject(Router),
      _catalog: inject(CatalogService),
      _culture: inject(CultureStore),
      _usage: inject(BackofficeUsage),
      _undo: inject(UndoStore),
      _translate: inject(TranslocoService),
      _labels: translateObjectSignal('catalogue.filter'),
      _params: toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap }),
      _query: toSignal(toObservable(store.text).pipe(debounceTime(250), distinctUntilChanged()), {
        initialValue: store.text(),
      }),
    };
  }),
  withComputed((store) => ({
    statusKey: computed(() => store._params().get('status') ?? 'Active'),
    selectedCode: computed(() => store._params().get('product')),
    page: computed(() => Math.max(1, Number(store._params().get('page') ?? 1) || 1)),
  })),
  withProps((store) => {
    const list = rxResource({
      params: () => ({
        status: store.statusKey(),
        page: store.page(),
        culture: store._culture.active(),
        text: store._query(),
      }),
      stream: ({ params }) =>
        store._catalog.list(
          params.status === 'all' ? null : (params.status as ProductStatus),
          params.culture,
          params.page,
          PageSize,
          params.text,
        ),
    });
    return {
      _list: list,
      ...readStatus(list),
      /** The products, their status replaced one by one as changes come back. */
      products: linkedSignal(() => valueOf(list)?.items ?? []),
      /**
       * How many match the text in each status, kept while the next answer loads.
       * Every chip says its number whichever is chosen: when only the chosen one
       * did, a click moved a count from one chip to another, the row changed width
       * and the search box beside it jumped (the author, 2026-09-28).
       */
      _counts: keptCounts(() => valueOf(list)?.counts),
    };
  }),
  withComputed((store) => {
    const total = computed(() => valueOf(store._list)?.total ?? 0);
    return {
      total,
      _pages: computed(() => Math.max(1, Math.ceil(total() / PageSize))),
      filters: computed<FilterOption[]>(() =>
        statusChips(Statuses, store._labels() as Record<string, string>, store._counts()),
      ),
      selected: computed(() => store.products().find((item) => item.code === store.selectedCode()) ?? null),
    };
  }),
  withMethods((store) => {
    const navigate = (queryParams: Record<string, string | number | null>) =>
      void store._router.navigate([], { queryParams, queryParamsHandling: 'merge' });

    /** One change: busy while it goes, the row's status from the server's answer. */
    const change = (
      item: ProductSummary,
      request: Observable<{ status: ProductStatus }>,
      fallback: ProductStatus,
      done?: () => void,
    ) => {
      patchState(store, { busy: item.productId, refusal: null });
      request.subscribe({
        next: (answer) => {
          patchState(store, { busy: null });
          const status = answer.status ?? fallback;
          store.products.update((items) =>
            items.map((row) => (row.productId === item.productId ? { ...row, status } : row)),
          );
          done?.();
        },
        error: (failure: unknown) => {
          patchState(store, { busy: null });
          if (isSessionExpired(failure)) return;
          patchState(store, { refusal: '' });
        },
      });
    };

    const publish = (item: ProductSummary) =>
      change(
        item,
        store._catalog.publish(item.productId).pipe(store._usage.reported('catalogue', 'publish', 'pane')),
        'Active',
      );

    return {
      search(text: string): void {
        patchState(store, { text });
      },
      filterBy(status: string): void {
        navigate({ status: status === 'Active' ? null : status, page: null });
      },
      /** A page past the last is the last. */
      go(page: number): void {
        navigate({ page: page <= 1 ? null : Math.min(page, store._pages()) });
      },
      open(item: ProductSummary): void {
        navigate({ product: item.code });
      },
      close(): void {
        navigate({ product: null });
      },
      publish,
      /** Withdrawing offers Undo, which is publishing it again — on the server, audited like any other. */
      withdraw(item: ProductSummary): void {
        change(
          item,
          store._catalog.withdraw(item.productId).pipe(store._usage.reported('catalogue', 'withdraw', 'pane')),
          'Draft',
          () =>
            store._undo.offerTo(store._translate.translate('catalogue.withdrawn', { name: item.name }), () =>
              publish({ ...item, status: 'Draft' }),
            ),
        );
      },
      importSeed(): void {
        patchState(store, { importing: true, imported: null });
        store._catalog
          .import()
          .pipe(store._usage.reported('catalogue', 'import', 'page'))
          .subscribe({
            next: (result) => {
              patchState(store, { importing: false, imported: result });
              store._list.reload();
            },
            error: (failure: unknown) => {
              patchState(store, { importing: false });
              if (isSessionExpired(failure)) return;
              patchState(store, { refusal: '' });
            },
          });
      },
    };
  }),
);
