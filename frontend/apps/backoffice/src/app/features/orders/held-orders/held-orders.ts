import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { DateTimePipe, Notice } from '@tendero/shared-ui';
import { HeldOrdersStore } from './held-orders.store';

/**
 * Paid orders a channel took that Tendero could not receive (P16-29), above the orders
 * they should have been among, and only when there are any. Each says why in the
 * reader's language — composed from the code, not the server's sentence — since when,
 * and links to the order in the channel's admin, where it is mended or closed. No
 * dismiss: a held order leaves only by being received or closed there.
 */
@Component({
  selector: 'backoffice-held-orders',
  imports: [TranslocoDirective, Notice, DateTimePipe],
  templateUrl: './held-orders.html',
  host: { class: 'block' },
  providers: [HeldOrdersStore],
})
export class HeldOrders {
  protected readonly store = inject(HeldOrdersStore);
  protected readonly culture = inject(CultureStore);
}
