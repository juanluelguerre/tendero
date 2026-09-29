import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ShopLinks } from '../../shop-links';
import { ContinueCheckoutStore } from './continue-checkout.store';

/**
 * Where an agent sends its person when the shop cannot take the agent's payment
 * (UCP's requires_escalation, P11-1): the basket the agent chose is taken over
 * and the person pays for it here. Paying completes the agent's checkout, so the
 * agent reads the order it asked for.
 *
 * The link's id is the credential, as a guest's order id is: nothing to sign in
 * to, and nothing handed over but the basket.
 */
@Component({
  selector: 'storefront-continue-checkout-page',
  imports: [TranslocoDirective, RouterLink],
  providers: [ContinueCheckoutStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './continue-checkout-page.html',
  styleUrl: './continue-checkout-page.css',
})
export class ContinueCheckoutPage {
  protected readonly store = inject(ContinueCheckoutStore);
  protected readonly links = inject(ShopLinks);

  constructor() {
    void this.store.takeOver();
  }
}
