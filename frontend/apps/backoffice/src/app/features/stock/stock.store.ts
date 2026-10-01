import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { translateObjectSignal } from '@jsverse/transloco';
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
import type { SkuDescription, StockFilter, StockList, StockRow } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { without } from '@tendero/shared-util';
import { catchError, debounceTime, distinctUntilChanged, map, type Observable, of, switchMap } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { InventoryService } from '../../data-access/inventory.service';
import { readStatus } from '../../read-status';
import { BackofficeUsage } from '../../usage';
import type { FilterOption } from '../../worklist/filter-chips/filter-chips';
import { withWorklistUrl } from '../../worklist/with-worklist-url';
import { extendResource } from '@ngrx/signals/resource';

/** The chips, in the order the job meets them: what to restock first. */
const Filters: readonly StockFilter[] = ['Restock', 'SoldOut', 'All'];

/** A row's identity: a SKU on a shelf of one warehouse. */
export function shelfOf(row: StockRow): string {
  return `${row.sku}@${row.warehouseCode}`;
}

/**
 * The shelves' state (ADR 0047). The filter, the search and the page live in the
 * URL, as every list's do (ADR 0042), so the dashboard's *To restock* figure can
 * open this screen exactly there. The rows come from Inventory and their names
 * from the catalogue — a second call, and the boundary rather than a
 * shortcoming: Inventory references the SharedKernel and nothing else, so it
 * cannot name a product (ADR 0024). The two contexts meet only here, where a
 * person is looking at both. A count is saved one shelf at a time and the row
 * shows the server's numbers, never the typed one.
 */
export const StockStore = signalStore(
  withState(() => ({
    /** What is typed, from the URL's `q` the first time. */
    text: inject(ActivatedRoute).snapshot.queryParamMap.get('q') ?? '',
    /** The counts typed and not yet saved, per shelf. */
    drafts: {} as Record<string, number>,
    saving: null as string | null,
    saved: null as string | null,
    saveFailed: false,
  })),
  withWorklistUrl(),
  withProps((store) => {
    return {
      _inventory: inject(InventoryService),
      _catalog: inject(CatalogService),
      _culture: inject(CultureStore),
      _usage: inject(BackofficeUsage),
      _labels: translateObjectSignal('stock.filter'),
      _query: toSignal(toObservable(store.text).pipe(debounceTime(250), distinctUntilChanged()), {
        initialValue: store.text(),
      }),
    };
  }),
  withComputed((store) => ({
    filter: computed<StockFilter>(() => {
      // Read in any case, as the server reads it: `restock` is a link a person could type.
      const asked = store._params().get('filter')?.toLowerCase();
      return Filters.find((filter) => filter.toLowerCase() === asked) ?? 'All';
    }),
    searching: computed(() => store._query().trim().length > 0),
  })),
  withProps((store) => {
    /**
     * The shelves for the filter and the search, reloaded when the language
     * changes: the warehouse labels come from the server in the requested culture.
     * A search asks the catalogue first, because a product's name is the
     * catalogue's: it answers with the SKUs it matched, and the ledger shows those
     * and any whose SKU contains the text (R-7.1).
     */
    const list = extendResource(
      rxResource({
        params: () => ({
          culture: store._culture.active(),
          filter: store.filter(),
          text: store._query().trim(),
          page: store.page(),
        }),
        stream: ({ params }): Observable<StockList> =>
          params.text
            ? store._catalog.skusMatching(params.text, params.culture).pipe(
                map((found) => found.items.map((item) => item.sku)),
                // A catalogue that cannot answer leaves the SKU half of the search
                // working: the shelf does not depend on the catalogue being up.
                catchError(() => of<string[]>([])),
                switchMap((skus) =>
                  store._inventory.stock(
                    params.culture,
                    params.filter,
                    { text: params.text, skus },
                    params.page,
                    store.pageSize,
                  ),
                ),
              )
            : store._inventory.stock(params.culture, params.filter, undefined, params.page, store.pageSize),
      }),
    );

    /**
     * What each SKU is called. Not awaited before the grid renders: the numbers
     * are what the screen is for, and a failure here leaves the SKUs unlabelled
     * and the grid working. Distinct SKUs, because two warehouses hold the same one.
     */
    const naming = extendResource(
      rxResource({
        params: () => {
          const skus = [...new Set((list.value()?.rows ?? []).map((row) => row.sku))];
          return skus.length === 0 ? undefined : { skus, culture: store._culture.active() };
        },
        stream: ({ params }) =>
          store._catalog
            .describeSkus(params.skus, params.culture)
            .pipe(catchError(() => of({ items: [] as SkuDescription[] }))),
      }),
    );

    return { _list: list, _naming: naming, ...readStatus(list) };
  }),
  // What a count changes and a new answer replaces: state, changed with patchState.
  withLinkedState(({ _list }) => ({
    /** The rows, replaced one by one as counts come back, until the list is asked again. */
    rows: () => _list.value()?.rows ?? [],
    /** The counts per filter, kept while the next page is on its way so the chips do not blink. */
    _counts: linkedSignal<ReadonlyMap<StockFilter, number> | undefined, ReadonlyMap<StockFilter, number> | null>({
      source: () => {
        const answered = _list.value();
        return answered && new Map(answered.counts.map((entry) => [entry.filter, entry.count]));
      },
      computation: (counts, previous) => counts ?? previous?.value ?? null,
    }),
  })),
  withComputed((store) => ({
    reservations: computed(() => store._list.value()?.reservations ?? []),
    restockAt: computed(() => store._list.value()?.restockAt ?? null),
    total: computed(() => store._list.value()?.total ?? 0),
    filters: computed<FilterOption[]>(() => {
      const labels = store._labels() as Record<string, string>;
      const counts = store._counts();
      return Filters.map((key) => ({ key, label: labels[key] ?? key, count: counts?.get(key) ?? null }));
    }),
    soldOut: computed(() => store._counts()?.get('SoldOut') ?? 0),
    _descriptions: computed<Record<string, SkuDescription>>(() =>
      Object.fromEntries((store._naming.value()?.items ?? []).map((item) => [item.sku, item])),
    ),
  })),
  withMethods((store) => {
    const navigate = store._navigate;

    return {
      search(text: string): void {
        patchState(store, { text });
      },
      filterBy(filter: string): void {
        store._usage.affordance('filter', 'stock');
        navigate({ filter: filter === 'All' ? null : filter, page: null });
      },
      /**
       * The product a row is holding, or null when the catalogue has never heard
       * of the SKU — a real state: stock can outlive a product, and a row that
       * quietly showed its SKU again would hide exactly the case to act on.
       */
      describe(row: StockRow): SkuDescription | null {
        return store._descriptions()[row.sku] ?? null;
      },
      changed(row: StockRow): boolean {
        const draft = store.drafts()[shelfOf(row)];
        return draft !== undefined && draft !== row.onHand;
      },
      draft(row: StockRow, onHand: number): void {
        patchState(store, (state) => ({ drafts: { ...state.drafts, [shelfOf(row)]: onHand }, saved: null }));
      },
      save(row: StockRow): void {
        const shelf = shelfOf(row);
        const onHand = store.drafts()[shelf];
        if (onHand === undefined || onHand === row.onHand || onHand < 0) return;

        patchState(store, { saving: shelf, saveFailed: false });
        store._inventory.count(row.sku, row.warehouseCode, onHand).subscribe({
          next: (counted) => {
            // The server's numbers, not the typed one: `available` is derived from
            // holds this screen does not track, and a count that collided with a
            // reservation would otherwise show a total nobody stored.
            patchState(store, (state) => ({
              rows: state.rows.map((candidate) =>
                shelfOf(candidate) === shelf
                  ? { ...candidate, onHand: counted.onHand, available: counted.available }
                  : candidate,
              ),
              drafts: without(state.drafts, shelf),
              saving: null,
              saved: shelf,
            }));
          },
          error: (failure: unknown) => {
            if (isSessionExpired(failure)) return;
            patchState(store, { saving: null, saveFailed: true });
          },
        });
      },
    };
  }),
  withHooks({
    onInit(store) {
      // The search goes into the URL too, replacing the entry rather than adding
      // one per keystroke, and is measured once per search actually run (R-7.1).
      // It follows what is typed, not the URL: the back button is not a keystroke.
      signalMethod<string>((q) => {
        if ((store._params().get('q') ?? '') === q) return;
        store._navigate({ q: q || null, page: null }, true);
        if (q) store._usage.affordance('search', 'stock');
      })(computed(() => store._query().trim()));
    },
  }),
);
