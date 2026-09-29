import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ReturnView } from '@tendero/shared-api';
import { RETURN_REASONS } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { ReturnsPolicyStore } from '../../returns/returns-policy.store';
import { ShopLinks } from '../../shop-links';
import { OrderStore } from './order.store';

/**
 * The order, and the way back.
 *
 * It is the confirmation page and the returns page at once, because they are the
 * same page at two different moments — and a shop that made you find a second
 * screen to send something back is a shop that makes you phone it instead.
 *
 * The return form only appears once the order is **Delivered**. That is not a UI
 * preference: `ReturnRequest.Open` refuses anything else, and offering a form
 * the server will reject is how an interface teaches people not to trust it.
 *
 * The order and the return being put together are its store's (ADR 0047);
 * the page presents them.
 */
@Component({
  selector: 'storefront-order',
  imports: [TranslocoDirective, FormsModule, RouterLink],
  providers: [OrderStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './order-page.html',
  styleUrl: './order-page.css',
})
export class OrderPage {
  protected readonly store = inject(OrderStore);

  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  private readonly culture = inject(CultureStore);

  protected readonly reasons = RETURN_REASONS;

  /** Days from delivery, published by the API so this page cannot disagree
      with the rule that refuses a late return. */
  protected readonly returnWindowDays = inject(ReturnsPolicyStore).windowDays;

  protected money(amount: number): string {
    return formatPrice(amount, this.store.order()?.currency ?? 'EUR', this.culture.active());
  }

  /** The first segment. A full GUID is unreadable and the URL has the rest. */
  protected shortId(id: string): string {
    return id.split('-')[0];
  }

  protected describe(request: ReturnView): string {
    return request.lines.map((line) => `${line.sku} ×${line.quantity}`).join(' · ');
  }
}
