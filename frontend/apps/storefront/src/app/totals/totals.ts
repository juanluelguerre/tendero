import { PricePipe } from '@tendero/shared-ui';
import { Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';

/**
 * Where the delivery stands in a total.
 *
 * - `at-checkout`: the basket, which has no address yet; the line says so, quietly.
 * - `to-choose`: the checkout before a rate is picked; the same words, in the
 *   line's own voice, because choosing is the next thing on the page.
 * - `charged`: a figure, the one the quote or the order carries.
 */
export type ShippingState = 'at-checkout' | 'to-choose' | 'charged';

/**
 * What an order costs, in the shape the cart, the checkout and the order all
 * show it: subtotal, discounts when there are any, delivery, total, and the tax
 * the total includes.
 *
 * It formats and never computes: every figure is the server's (a quote's or an
 * order's), because a client that added them up would disagree with the server
 * the first time a promotion touched a line (ADR 0016). What sits around it — a
 * link to checkout, the pay button, the address — is each page's own.
 */
@Component({
  selector: 'storefront-totals',
  imports: [TranslocoDirective, PricePipe],
  templateUrl: './totals.html',
  host: { class: 'block' },
})
export class Totals {
  readonly currency = input.required<string>();
  readonly subtotal = input.required<number>();
  readonly discountTotal = input(0);
  readonly shipping = input(0);
  readonly shippingState = input<ShippingState>('charged');
  /** The rate's own name once an order has one ("Envío estándar"); "Delivery" otherwise. */
  readonly shippingLabel = input<string | null>(null);
  readonly total = input.required<number>();
  readonly taxTotal = input(0);

  protected readonly culture = inject(CultureStore);
}
