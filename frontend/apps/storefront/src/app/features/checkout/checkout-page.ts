import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import type { AddressRequest, ShippingOptionView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { CartStore } from '../../data-access/cart.service';
import { OrderService } from '../../data-access/order.service';
import { ShopLinks } from '../../shop-links';
import { checkoutProblem } from './checkout-problem';

/**
 * The test cards the fake provider understands.
 *
 * They are on the screen on purpose. The provider is a fake and the demo is the
 * deliverable, so the four outcomes a shop has to handle — paid, declined,
 * provider down, and authorised-then-failed — need to be one click apart. A real
 * adapter replaces this block with a card field and nothing else changes.
 */
const CARDS = ['card-ok', 'card-declined', 'card-unreachable', 'card-capture-fails'] as const;

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
 */
@Component({
  selector: 'storefront-checkout',
  imports: [TranslocoDirective, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './checkout-page.html',
  styleUrl: './checkout-page.css',
})
export class CheckoutPage {
  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  protected readonly store = inject(CartStore);
  private readonly culture = inject(CultureStore);
  private readonly orders = inject(OrderService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  protected readonly cards = CARDS;

  /**
   * The address is prefilled with somewhere the shop actually delivers, because
   * this is a demo catalogue and making a reviewer type an address to see a
   * checkout is a way to lose them. A real shop starts empty, or from the
   * account's book.
   *
   * **The name is not.** It used to be "Ana Ruiz" — the seeded shopper's name —
   * for every guest, so the backoffice listed every guest order as hers while
   * her account showed one (UI review, 2026-09-11). It is the signed-in
   * person's name, or whatever a guest types.
   */
  protected address: AddressRequest = {
    recipientName: inject(AuthStore).identity()?.name ?? '',
    line1: 'Calle Mayor 1',
    line2: null,
    city: 'Madrid',
    region: null,
    postalCode: '28013',
    countryCode: 'ES',
    phone: null,
  };

  protected readonly options = signal<ShippingOptionView[]>([]);
  protected readonly chosen = signal<ShippingOptionView | null>(null);
  protected readonly instrument = signal<string>(CARDS[0]);
  protected readonly rating = signal(false);
  protected readonly paying = signal(false);
  protected readonly problem = signal<string | null>(null);

  /** The notice above the form, when there is one. */
  private readonly notice = viewChild<ElementRef<HTMLElement>>('notice');

  /**
   * One key per attempt at THIS basket, minted once. Pressing pay twice, a
   * proxy retrying a timeout and an agent reissuing the request are all the same
   * key, and all three must be one order.
   */
  private readonly idempotencyKey = crypto.randomUUID();

  private readonly currency = computed(() => this.store.quote()?.currency ?? 'EUR');

  constructor() {
    void this.start();

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

  protected money(amount: number): string {
    return formatPrice(amount, this.currency(), this.culture.active());
  }

  protected cardKey(card: string): string {
    return `checkout.${card.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase())}`;
  }

  private async start(): Promise<void> {
    await this.store.load();
    if (!this.store.isEmpty()) await this.loadOptions();
  }

  protected async loadOptions(): Promise<void> {
    // The whole address, recipient included: the server validates it as one,
    // and asking with a blank name answered 400 — which this page reads as
    // "we do not deliver there" (found live, 2026-09-11, the day the name
    // stopped being prefilled for guests).
    if (!this.address.recipientName.trim() || !this.address.line1 || !this.address.city || !this.address.postalCode)
      return;

    this.rating.set(true);
    this.problem.set(null);

    try {
      const response = await this.store.shippingOptions(this.address);

      this.options.set(response.options);

      // Preselect the cheapest, which the port guarantees is first. A checkout
      // with nothing chosen is a checkout with no total.
      if (response.options.length > 0) await this.choose(response.options[0]);
    } catch (error) {
      this.options.set([]);
      this.chosen.set(null);

      // "We do not deliver there yet" is a business fact, and the one refusal
      // on this page worth saying in the shopper's own language rather than
      // relaying the server's English sentence.
      this.problem.set(
        (error as { status?: number }).status === 400
          ? this.transloco.translate('checkout.unserved', { country: this.address.countryCode })
          : this.describe(error),
      );
    } finally {
      this.rating.set(false);
    }
  }

  /** A guest's name arrives last, so it is what completes the address: the
      options load then, rather than waiting for a button nobody knows to press. */
  protected async nameEntered(): Promise<void> {
    if (this.options().length === 0) await this.loadOptions();
  }

  /**
   * Choosing re-quotes, because `FreeShipping` is a promotion effect and the
   * total the shopper agrees to includes delivery. The fingerprint moves with
   * it, which is exactly what makes the mismatch check meaningful.
   */
  protected async choose(option: ShippingOptionView): Promise<void> {
    this.chosen.set(option);
    await this.store.quoteWithShipping(option.amount);
  }

  protected async pay(): Promise<void> {
    const option = this.chosen();
    const quote = this.store.quote();

    if (!option || !quote) return;

    this.paying.set(true);
    this.problem.set(null);

    try {
      const order = await this.orders.place(
        {
          shippingAddress: this.address,
          shippingOptionCode: option.code,
          quoteHash: quote.inputHash,
          instrument: this.instrument(),
          idempotencyKey: this.idempotencyKey,
        },
        this.store.token,
        this.culture.active(),
      );

      // The basket became an order. Clearing the token is what stops the next
      // page load asking for a cart that is gone.
      this.store.clear();

      await this.router.navigate(this.links.order(order.orderId), { state: { order } });
    } catch (error) {
      this.problem.set(this.describe(error));

      // The price moved. Re-quoting here means the summary and the button show
      // the NEW total immediately, so the second press agrees to what is on
      // screen rather than to what was.
      if ((error as { status?: number })?.status === 409) await this.choose(option);
    } finally {
      this.paying.set(false);
    }
  }

  /** One sentence a shopper can act on — see `checkoutProblem`. */
  private describe(error: unknown): string {
    return checkoutProblem(
      error as { status?: number; error?: unknown },
      (key, params) => this.transloco.translate(key, params),
      (amount) => this.money(amount),
    );
  }
}
