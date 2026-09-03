import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ReservationRow, StockRow } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { without } from '@tendero/shared-util';
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

  protected readonly outOfStock = computed(
    () => this.rows().filter((row) => row.available === 0).length,
  );

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
      error: () => {
        this.saving.set(null);
        this.saveFailed.set(true);
      },
    });
  }

  constructor() {
    this.inventory.stock(this.culture.active()).subscribe({
      next: (result) => {
        this.rows.set(result.rows);
        this.reservations.set(result.reservations);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }
}
