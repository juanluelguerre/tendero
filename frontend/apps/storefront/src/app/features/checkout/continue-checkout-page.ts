import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CartStore } from '../../data-access/cart.service';
import { OrderService } from '../../data-access/order.service';
import { ShopLinks } from '../../shop-links';

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
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './continue-checkout-page.html',
  styleUrl: './continue-checkout-page.css',
})
export class ContinueCheckoutPage implements OnInit {
  protected readonly links = inject(ShopLinks);
  protected readonly state = signal<'taking-over' | 'gone' | 'failed'>('taking-over');

  private readonly orders = inject(OrderService);
  private readonly cart = inject(CartStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  async ngOnInit(): Promise<void> {
    const sessionId = this.route.snapshot.paramMap.get('sessionId') ?? '';

    try {
      const handOff = await this.orders.handOff(sessionId);
      await this.cart.adopt(handOff.cartToken);
      // In the language the agent checked out in: the basket's names and the
      // order's language follow it.
      await this.router.navigate(['/', handOff.culture, 'checkout'], { replaceUrl: true });
    } catch (error) {
      this.state.set((error as { status?: number })?.status === 404 ? 'gone' : 'failed');
    }
  }
}
