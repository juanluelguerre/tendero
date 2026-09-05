import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ReservationRow, SkuDescription, StockRow } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { without } from '@tendero/shared-util';
import type { Subscription } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { InventoryService } from '../../data-access/inventory.service';

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
@Component({
  selector: 'backoffice-stock',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stock-page.html',
  styleUrl: './stock-page.css',
})
export class StockPage {
  private readonly inventory = inject(InventoryService);
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);

  protected readonly rows = signal<StockRow[]>([]);
  protected readonly reservations = signal<ReservationRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

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
   */
  private readonly descriptions = signal<Record<string, SkuDescription>>({});

  protected readonly outOfStock = computed(
    () => this.rows().filter((row) => row.available === 0).length,
  );

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

  /** The requests being answered, so a language switch cancels them rather than racing them. */
  private inFlight?: Subscription;
  private naming?: Subscription;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.inFlight?.unsubscribe();
      this.naming?.unsubscribe();
    });

    // Reloaded when the language changes: the warehouse labels and the product
    // names come from the server in the requested culture, and a grid that kept
    // its Spanish names under English headings would be the review queue's bug
    // repeated one tab over.
    effect(() => this.load(this.culture.active()));
  }

  private load(culture: string): void {
    this.inFlight?.unsubscribe();
    this.naming?.unsubscribe();
    this.loading.set(true);
    this.failed.set(false);

    this.inFlight = this.inventory.stock(culture).subscribe({
      next: (result) => {
        this.rows.set(result.rows);
        this.reservations.set(result.reservations);
        this.loading.set(false);
        this.name(result.rows, culture);
      },
      error: (failure: unknown) => {
        if (isSessionExpired(failure)) return;

        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  /**
   * Fills in the product names, after the grid is already on screen.
   *
   * Deliberately not awaited before rendering: the numbers are what the screen
   * is FOR, and holding a stocktake behind a second request would make the
   * catalogue's availability a prerequisite for counting a shelf — which is the
   * dependency the boundary exists to refuse. The names arrive a moment later
   * and the column fills in.
   *
   * Distinct SKUs, because two warehouses hold the same one and asking twice
   * for the same answer is the N+1 the endpoint takes a set to avoid.
   */
  private name(rows: readonly StockRow[], culture: string): void {
    const skus = [...new Set(rows.map((row) => row.sku))];
    if (skus.length === 0) return;

    this.naming = this.catalog.describeSkus(skus, culture).subscribe({
      next: (result) =>
        this.descriptions.set(
          Object.fromEntries(result.items.map((item) => [item.sku, item])),
        ),
      // A failure here leaves the SKUs unlabelled and the grid working. The
      // shelf numbers do not depend on the catalogue being reachable, and a
      // screen that broke because a name could not be fetched would have made
      // them depend on it.
      error: () => undefined,
    });
  }
}
