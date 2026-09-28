import { ChangeDetectionStrategy, Component, computed, effect, inject, linkedSignal, signal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import type { ReservationRow, SkuDescription, StockFilter, StockList, StockRow } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { without } from '@tendero/shared-util';
import { catchError, debounceTime, distinctUntilChanged, map, type Observable, of, switchMap } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { InventoryService } from '../../data-access/inventory.service';
import { BackofficeUsage } from '../../usage';
import { FilterChips, type FilterOption } from '../../work/filter-chips';
import { Pager } from '../../work/pager';

/** The document's set, in the chips' order: the job first (R-7.1). */
const Filters: readonly StockFilter[] = ['Restock', 'SoldOut', 'All'];

/**
 * What is on every shelf, and what is being held.
 *
 * The first screen in the backoffice that WRITES, and the shape of it is the
 * argument: a count is not an increment. You type what the shelf actually holds
 * and the difference is the system's to work out — which is why pressing save
 * twice changes nothing the second time, and why a stocktake and a delivery are
 * different words in the domain.
 *
 * The reservations panel sits beside the grid rather than on its own route
 * because it answers the question the grid provokes. "2 available" out of five
 * on hand is not information until you can see the three that are held, for
 * which order, and — when the row is `Released` — the sentence explaining why
 * inventory said no.
 *
 * Editing is per row and optimistic in one direction only: the response carries
 * the recomputed availability, so the row is replaced with what the server
 * actually stored rather than with what was typed. `available` is derived
 * server-side and a grid that subtracted for itself would disagree with the
 * search index the moment a hold expired.
 */
/** Twenty rows a page, as every list of work (R-7.2). */
const PageSize = 20;

@Component({
  selector: 'backoffice-stock',
  imports: [TranslocoDirective, FilterChips, Pager],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stock-page.html',
  styleUrl: './stock-page.css',
})
export class StockPage {
  private readonly inventory = inject(InventoryService);
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);
  private readonly usage = inject(BackofficeUsage);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly filterLabels = translateObjectSignal('stock.filter');
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  /**
   * The filter and the search live in the URL, as every list's do (ADR 0042), so
   * the dashboard's *To restock* figure can open this screen exactly there.
   */
  protected readonly filter = computed<StockFilter>(() => {
    // Read in any case, as the server reads it: `restock` is a link a person could type.
    const asked = this.params().get('filter')?.toLowerCase();
    return Filters.find((filter) => filter.toLowerCase() === asked) ?? 'All';
  });

  /** A SKU, or a product's name in any language; asked for a moment after the typing stops. */
  protected readonly text = signal(this.route.snapshot.queryParamMap.get('q') ?? '');
  private readonly query = toSignal(toObservable(this.text).pipe(debounceTime(250), distinctUntilChanged()), {
    initialValue: this.text(),
  });

  /**
   * The shelves for the filter and the search, reloaded when the language
   * changes: the warehouse labels come from the server in the requested culture.
   *
   * A search asks the catalogue first, because a product's name is the
   * catalogue's: it answers with the SKUs it matched, and the ledger shows those
   * and any whose SKU contains the text (R-7.1). The two contexts still meet
   * only here, where a person is looking at both.
   */
  /** The page in the URL, of SKUs rather than rows: a SKU's warehouses stay together (R-7.2). */
  protected readonly page = computed(() => Math.max(1, Number(this.params().get('page') ?? 1) || 1));
  protected readonly pageSize = PageSize;

  private readonly list = rxResource({
    params: () => ({ culture: this.culture.active(), filter: this.filter(), text: this.query().trim(), page: this.page() }),
    stream: ({ params }): Observable<StockList> =>
      params.text
        ? this.catalog.skusMatching(params.text, params.culture).pipe(
            map((found) => found.items.map((item) => item.sku)),
            // A catalogue that cannot answer leaves the SKU half of the search
            // working: the shelf does not depend on the catalogue being up.
            catchError(() => of<string[]>([])),
            switchMap((skus) => this.inventory.stock(params.culture, params.filter, { text: params.text, skus }, params.page, PageSize)),
          )
        : this.inventory.stock(params.culture, params.filter, undefined, params.page, PageSize),
  });

  /** The rows, which a count replaces one at a time with what the server stored. */
  protected readonly rows = linkedSignal(() => this.list.value()?.rows ?? []);
  protected readonly reservations = computed(() => this.list.value()?.reservations ?? []);
  protected readonly restockAt = computed(() => this.list.value()?.restockAt ?? null);
  protected readonly total = computed(() => this.list.value()?.total ?? 0);
  protected readonly loading = computed(() => this.list.isLoading() && !this.list.hasValue());
  protected readonly failed = computed(() => this.list.status() === 'error');
  protected readonly searching = computed(() => this.query().trim().length > 0);

  /** Every filter's number, kept while the next answer loads, so no chip changes size on a click. */
  private readonly counts = linkedSignal<ReadonlyMap<StockFilter, number> | undefined, ReadonlyMap<StockFilter, number> | null>({
    source: () => {
      const list = this.list.value();
      return list && new Map(list.counts.map((entry) => [entry.filter, entry.count]));
    },
    computation: (counts, previous) => counts ?? previous?.value ?? null,
  });

  protected readonly filters = computed<FilterOption[]>(() => {
    const labels = this.filterLabels() as Record<string, string>;
    const counts = this.counts();
    return Filters.map((key) => ({ key, label: labels[key] ?? key, count: counts?.get(key) ?? null }));
  });

  protected readonly soldOut = computed(() => this.counts()?.get('SoldOut') ?? 0);

  /** Which row is in flight, which one just landed, and whether one blew up. */
  protected readonly saving = signal<string | null>(null);
  protected readonly saved = signal<string | null>(null);
  protected readonly saveFailed = signal(false);

  /** What has been typed but not saved, keyed by row. */
  private readonly drafts = signal<Record<string, number>>({});

  /**
   * What each SKU is called, keyed by SKU.
   *
   * It comes from a SECOND call, to the catalogue, and that is the boundary
   * rather than a shortcoming: `Inventory` may reference the SharedKernel and
   * nothing else, so `/api/inventory/stock` structurally cannot name a product.
   * Stock exists without a catalogue exactly as it exists without orders
   * (ADR 0024). The grid asks both and joins them here, which is what a screen
   * is for.
   *
   * Not awaited before the grid renders: the numbers are what the screen is
   * FOR, and a failure here leaves the SKUs unlabelled and the grid working.
   * Distinct SKUs, because two warehouses hold the same one.
   */
  private readonly naming = rxResource({
    params: () => {
      const skus = [...new Set((this.list.value()?.rows ?? []).map((row) => row.sku))];
      return skus.length === 0 ? undefined : { skus, culture: this.culture.active() };
    },
    stream: ({ params }) =>
      this.catalog.describeSkus(params.skus, params.culture).pipe(catchError(() => of({ items: [] as SkuDescription[] }))),
  });

  private readonly descriptions = computed<Record<string, SkuDescription>>(() =>
    Object.fromEntries((this.naming.value()?.items ?? []).map((item) => [item.sku, item])),
  );

  constructor() {
    // The search goes into the URL too, replacing the entry rather than adding
    // one per keystroke, and is measured once per search actually run (R-7.1).
    effect(() => {
      const q = this.query().trim();
      if ((this.params().get('q') ?? '') === q) return;
      void this.router.navigate([], { queryParams: { q: q || null, page: null }, queryParamsHandling: 'merge', replaceUrl: true });
      if (q) this.usage.affordance('search', 'stock');
    });
  }

  protected filterBy(filter: string): void {
    this.usage.affordance('filter', 'stock');
    void this.router.navigate([], { queryParams: { filter: filter === 'All' ? null : filter, page: null }, queryParamsHandling: 'merge' });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page: page <= 1 ? null : page }, queryParamsHandling: 'merge' });
  }

  /**
   * The product a row is holding, or null when the catalogue has never heard of
   * the SKU.
   *
   * That null is a real state and the template renders it as one. The same rule
   * that lets Inventory ignore Catalog lets stock OUTLIVE a product: a shelf can
   * hold something that was archived, or that arrived before anybody catalogued
   * it. A row that quietly showed its SKU again would hide exactly the case a
   * shopkeeper needs to act on.
   */
  protected describe(row: StockRow): SkuDescription | null {
    return this.descriptions()[row.sku] ?? null;
  }

  protected key(row: StockRow): string {
    return `${row.sku}@${row.warehouseCode}`;
  }

  /**
   * Order ids are GUIDs and the panel is 300px wide. The first segment is enough
   * to match a row against an order somebody is looking at, and the full id is
   * one hover away in the title.
   */
  protected shortId(hold: ReservationRow): string {
    return hold.orderId.split('-')[0];
  }

  protected changed(row: StockRow): boolean {
    const draft = this.drafts()[this.key(row)];
    return draft !== undefined && draft !== row.onHand;
  }

  protected draft(row: StockRow, event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.drafts.update((drafts) => ({ ...drafts, [this.key(row)]: value }));
    this.saved.set(null);
  }

  protected save(row: StockRow): void {
    const key = this.key(row);
    const onHand = this.drafts()[key];

    if (onHand === undefined || onHand === row.onHand || onHand < 0) return;

    this.saving.set(key);
    this.saveFailed.set(false);

    this.inventory.count(row.sku, row.warehouseCode, onHand).subscribe({
      next: (counted) => {
        // The server's numbers, not the typed one: `available` is derived from
        // holds this screen does not track, and a count that collided with a
        // reservation would otherwise show a total nobody stored.
        this.rows.update((rows) =>
          rows.map((candidate) =>
            this.key(candidate) === key
              ? { ...candidate, onHand: counted.onHand, available: counted.available }
              : candidate,
          ),
        );

        this.drafts.update((current) => without(current, key));
        this.saving.set(null);
        this.saved.set(key);
      },
      error: (failure: unknown) => {
        if (isSessionExpired(failure)) return;

        this.saving.set(null);
        this.saveFailed.set(true);
      },
    });
  }
}
