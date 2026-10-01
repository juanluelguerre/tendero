import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ReservationRow } from '@tendero/shared-api';
import { orderReference } from '@tendero/shared-util';
import { StockStore } from '../stock.store';

/**
 * The panel that explains the grid: the reservations holding stock. Its most
 * interesting row is a refusal — a reservation that never held anything,
 * carrying the sentence inventory answered with.
 */
@Component({
  selector: 'backoffice-stock-holds',
  imports: [TranslocoDirective],
  templateUrl: './stock-holds.html',
  host: { class: 'block' },
})
export class StockHolds {
  protected readonly store = inject(StockStore);

  /**
   * Order ids are GUIDs and the panel is 300px wide. The reference the orders
   * screen shows — the id's random end — is enough to match a row against an
   * order somebody is looking at, and the full id is one hover away in the title.
   */
  protected shortId(hold: ReservationRow): string {
    return orderReference(hold.orderId);
  }
}
