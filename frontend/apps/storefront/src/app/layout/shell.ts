import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Culture, CultureStore } from '@tendero/shared-i18n';
import { CartStore } from '../data-access/cart.service';

/**
 * The storefront shell. It is NOT shared with the backoffice: one is roomy and
 * light and the other dense and dark, and unifying them would give a component
 * with a variant matrix. See docs/adr/0010.
 *
 * The header sticks. On a results page the search box is the thing people come
 * back to, and making them scroll up to reach it is the single most common way a
 * shop wastes a visit.
 */
@Component({
  selector: 'storefront-shell',
  imports: [RouterOutlet, RouterLink, TranslocoDirective],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  private readonly cultureStore = inject(CultureStore);

  protected readonly cultures = this.cultureStore.available;
  protected readonly culture = this.cultureStore.active;

  /**
   * Read once, here, so the badge is right on a cold load. Every page that
   * changes the cart updates the same store, so nothing else has to remember to
   * refresh it.
   */
  protected readonly cart = inject(CartStore);

  constructor() {
    void this.cart.load();
  }

  protected use(culture: Culture): void {
    this.cultureStore.use(culture);
  }
}
