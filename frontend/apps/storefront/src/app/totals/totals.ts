import { PricePipe } from '@tendero/shared-ui';
import type { Money } from '@tendero/shared-api';
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
 * Every figure is money as the API sent it — minor units and its currency — so
 * there is no currency to hand over beside them (phase 13b).
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
  readonly subtotal = input.required<Money>();
  readonly discountTotal = input<Money | null>(null);
  readonly shipping = input<Money | null>(null);
  readonly shippingState = input<ShippingState>('charged');
  /** The rate's own name once an order has one ("Envío estándar"); "Delivery" otherwise. */
  readonly shippingLabel = input<string | null>(null);
  readonly total = input.required<Money>();
  readonly taxTotal = input<Money | null>(null);

  protected readonly culture = inject(CultureStore);
}
