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
  template: `
    <section *transloco="let t">
      <header class="page-head">
        <h1 class="page-title">{{ t('stock.title') }}</h1>
        @if (outOfStock() > 0) {
          <span class="tag tag--danger">{{ t('stock.outOfStock', { count: outOfStock() }) }}</span>
        } @else if (rows().length > 0) {
          <span class="tag tag--ok">{{ t('stock.allAvailable') }}</span>
        }
      </header>
      <p class="page-hint">{{ t('stock.hint') }}</p>

      @if (loading()) {
        <p class="muted" role="status" aria-live="polite">{{ t('stock.loading') }}</p>
      } @else if (failed()) {
        <p class="failed" role="alert">{{ t('stock.failed') }}</p>
      } @else if (rows().length === 0) {
        <div class="empty"><p class="empty__text">{{ t('stock.empty') }}</p></div>
      } @else {
        <div class="grid">
          <div class="table-wrap">
            <table class="table">
              <caption class="sr-only">{{ t('stock.title') }}</caption>
              <thead>
                <tr>
                  <th scope="col">{{ t('stock.column.sku') }}</th>
                  <th scope="col">{{ t('stock.column.warehouse') }}</th>
                  <th scope="col" class="numeric">{{ t('stock.column.onHand') }}</th>
                  <th scope="col" class="numeric">{{ t('stock.column.reserved') }}</th>
                  <th scope="col" class="numeric">{{ t('stock.column.available') }}</th>
                  <th scope="col"><span class="sr-only">{{ t('stock.column.action') }}</span></th>
                </tr>
              </thead>
              <tbody>
                @for (row of rows(); track key(row)) {
                  <tr [class.row--empty]="row.available === 0">
                    <td class="numeric code">{{ row.sku }}</td>
                    <td>{{ row.warehouseName }}</td>
                    <td class="numeric">
                      <!-- The only editable cell on the screen. A count replaces
                           the number; it does not add to it. -->
                      <input
                        class="field field--count numeric"
                        type="number"
                        min="0"
                        step="1"
                        [attr.aria-label]="t('stock.countLabel', { sku: row.sku, warehouse: row.warehouseName })"
                        [value]="row.onHand"
                        [disabled]="saving() === key(row)"
                        (input)="draft(row, $event)"
                        (keydown.enter)="save(row)"
                      />
                    </td>
                    <td class="numeric held" [class.held--none]="row.reserved === 0">
                      {{ row.reserved }}
                    </td>
                    <td class="numeric available">{{ row.available }}</td>
                    <td class="actions">
                      @if (changed(row)) {
                        <button
                          class="button button--small"
                          type="button"
                          [disabled]="saving() === key(row)"
                          (click)="save(row)"
                        >
                          {{ saving() === key(row) ? t('stock.saving') : t('stock.save') }}
                        </button>
                      } @else if (saved() === key(row)) {
                        <span class="tag tag--ok">{{ t('stock.saved') }}</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
            @if (saveFailed()) {
              <p class="failed" role="alert">{{ t('stock.saveFailed') }}</p>
            }
          </div>

          <!-- The panel that explains the grid. Its most interesting row is a
               refusal: a reservation that never held anything, carrying the
               sentence inventory answered with. -->
          <aside class="holds">
            <h2 class="holds__title">{{ t('stock.reservations') }}</h2>

            @if (reservations().length === 0) {
              <p class="muted">{{ t('stock.noReservations') }}</p>
            } @else {
              <ul class="holds__list">
                @for (hold of reservations(); track hold.reservationId) {
                  <li class="hold">
                    <div class="hold__head">
                      <span class="hold__status" [class]="'hold__status--' + hold.status">
                        {{ t('stock.status.' + hold.status) }}
                      </span>
                      <span class="hold__order">{{ shortId(hold) }}</span>
                    </div>

                    @if (hold.lines.length > 0) {
                      <ul class="hold__lines">
                        @for (line of hold.lines; track line) {
                          <li class="hold__line">{{ line }}</li>
                        }
                      </ul>
                    }

                    @if (hold.reason) {
                      <p class="hold__reason">{{ hold.reason }}</p>
                    }
                  </li>
                }
              </ul>
            }
          </aside>
        </div>
      }
    </section>
  `,
  styles: `
    /* The grid and its explanation, side by side while there is room. The
       panel drops under the table below 1100px rather than being scrolled
       horizontally with it: it is prose, not columns. */
    .grid {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 300px;
      gap: var(--space-5);
      align-items: start;
    }

    .code { color: var(--text); font-weight: 500; }

    /* Two of the three numbers carry meaning on sight: what is spoken for, and
       what is left. A zero available is the whole reason to open this screen.

       Zero held is deliberately NOT amber. Colouring every row's zero made the
       whole column read as an alert, which is the failure the design rules
       already name: colour that is always on carries no information. */
    .held { color: var(--warning); }
    .held--none { color: var(--text-subtle); }
    .available { color: var(--text); font-weight: 500; }
    .row--empty .available { color: var(--danger); }

    .field--count {
      width: 4.5rem;
      padding-block: 2px;
      text-align: end;
    }

    .actions { width: 5.5rem; text-align: end; }

    .holds {
      padding: var(--space-4);
      border: 1px solid var(--border);
      border-radius: var(--radius-lg);
      background: var(--bg-surface);
    }
    .holds__title {
      margin: 0 0 var(--space-3);
      font-size: var(--text-xs);
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: var(--tracking-wide);
      color: var(--text-muted);
    }
    .holds__list { margin: 0; padding: 0; list-style: none; display: grid; gap: var(--space-3); }

    .hold {
      padding-block-end: var(--space-3);
      border-block-end: 1px solid var(--border);
      font-size: var(--text-xs);
    }
    .hold:last-child { padding-block-end: 0; border-block-end: 0; }

    .hold__head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); }
    .hold__status {
      padding: 1px var(--space-2);
      border: 1px solid currentColor;
      border-radius: var(--radius-sm);
      font-size: var(--text-3xs);
    }
    .hold__status--Held { color: var(--warning); }
    .hold__status--Committed { color: var(--positive); }
    .hold__status--Released { color: var(--danger); }
    .hold__status--Expired { color: var(--text-subtle); }

    .hold__order { font-family: var(--font-mono); font-size: var(--text-3xs); color: var(--text-subtle); }

    .hold__lines { margin: var(--space-2) 0 0; padding: 0; list-style: none; }
    .hold__line { font-family: var(--font-mono); font-size: var(--text-3xs); color: var(--text-muted); }

    /* The sentence that makes a cancelled order explicable. */
    .hold__reason { margin: var(--space-2) 0 0; color: var(--text-muted); }

    @media (max-width: 1100px) {
      .grid { grid-template-columns: minmax(0, 1fr); }
    }
  `,
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
