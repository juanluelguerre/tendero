import { CultureStore } from '@tendero/shared-i18n';
import { Component, effect, ElementRef, inject, viewChild } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CartStore } from '../../cart/cart.store';
import { ShopLinks } from '../../shop-links';
import { CheckoutCards, CheckoutStore, NewAddress, NewCard } from './checkout.store';
import { CountryPipe, Notice, PricePipe } from '@tendero/shared-ui';
import { ShippingCountriesStore } from '../../addresses/shipping-countries.store';
import { Totals } from '../../totals/totals';
import { Summary } from '../../summary/summary';

/**
 * Address, delivery, payment. One page, three steps, in the order the shop
 * needs them: a rate is a function of a destination, and a total is a function
 * of a rate.
 *
 * The interesting state is the **409**. When the price moved between the quote
 * and the till, the server answers with the new fingerprint and the new total,
 * and this page says what changed rather than just refusing. That path is what
 * a quote's fingerprint exists for (ADR 0016), and it is the same mechanism an
 * AP2 mandate check will use in phase 11.
 *
 * The state is its store's (ADR 0047) and the basket the shop's one cart; the
 * page presents both, and brings a refusal into view.
 */
@Component({
  selector: 'storefront-checkout',
  imports: [TranslocoDirective, RouterLink, Notice, Totals, Summary, PricePipe, CountryPipe, FormField],
  providers: [CheckoutStore],
  templateUrl: './checkout-page.html',
})
export class CheckoutPage {
  protected readonly culture = inject(CultureStore);
  protected readonly store = inject(CheckoutStore);
  protected readonly cart = inject(CartStore);
  protected readonly countries = inject(ShippingCountriesStore);

  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  protected readonly cards = CheckoutCards;

  /** Where an address typed here goes, as the radio rows name it. */
  protected readonly newAddress = NewAddress;

  /** How it is paid with one of the provider's test cards, as the radio rows name it. */
  protected readonly newCard = NewCard;

  /** The notice above the form, when there is one. */
  private readonly notice = viewChild('notice', { read: ElementRef });

  constructor() {
    void this.store.start();

    // The notice sits above the form and the pay button at the bottom of it,
    // so a declined card used to be announced to a screen reader and shown to
    // nobody else: the shopper was scrolled down beside a button that simply
    // stopped spinning (UI review, 2026-09-11). It is brought into view and
    // given focus, so the next key press reads it too.
    effect(() => {
      const notice = this.notice()?.nativeElement;
      if (!notice) return;

      notice.scrollIntoView({ block: 'center' });
      notice.focus({ preventScroll: true });
    });
  }

  protected cardKey(card: string): string {
    return `checkout.${card.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase())}`;
  }
}
