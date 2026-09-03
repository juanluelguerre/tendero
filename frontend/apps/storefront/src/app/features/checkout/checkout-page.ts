import { HttpClient, HttpParams } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import type {
  AddressRequest,
  OrderView,
  PriceChangedResponse,
  ShippingOptionView,
} from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { API_BASE_URL, formatPrice } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';
import { CartStore } from '../../data-access/cart.service';

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
  template: `
    <section *transloco="let t" class="checkout">
      <h1 class="checkout__title">{{ t('checkout.title') }}</h1>

      @if (store.isEmpty()) {
        <div class="blank">
          <p class="blank__text">{{ t('cart.empty') }}</p>
          <a class="button" routerLink="/">{{ t('nav.continueShopping') }}</a>
        </div>
      } @else {
        @if (problem(); as message) {
          <p class="notice notice--bad" role="alert">{{ message }}</p>
        }

        <div class="layout">
          <div class="steps">
            <!-- 1 · Where -->
            <fieldset class="step">
              <legend class="step__title">{{ t('checkout.address') }}</legend>

              <div class="grid">
                <label class="row row--wide">
                  <span class="row__label">{{ t('checkout.recipientName') }}</span>
                  <input class="field" name="recipientName" [(ngModel)]="address.recipientName" autocomplete="name" />
                </label>

                <label class="row row--wide">
                  <span class="row__label">{{ t('checkout.line1') }}</span>
                  <input class="field" name="line1" [(ngModel)]="address.line1" autocomplete="address-line1" />
                </label>

                <label class="row row--wide">
                  <span class="row__label">{{ t('checkout.line2') }}</span>
                  <input class="field" name="line2" [(ngModel)]="address.line2" autocomplete="address-line2" />
                </label>

                <label class="row">
                  <span class="row__label">{{ t('checkout.postalCode') }}</span>
                  <input class="field" name="postalCode" [(ngModel)]="address.postalCode" autocomplete="postal-code" />
                </label>

                <label class="row">
                  <span class="row__label">{{ t('checkout.city') }}</span>
                  <input class="field" name="city" [(ngModel)]="address.city" autocomplete="address-level2" />
                </label>

                <label class="row">
                  <span class="row__label">{{ t('checkout.region') }}</span>
                  <input class="field" name="region" [(ngModel)]="address.region" autocomplete="address-level1" />
                </label>

                <label class="row">
                  <span class="row__label">{{ t('checkout.country') }}</span>
                  <!-- Two letters, uppercase: the value three different things
                       look up (a rate, a tax rule, a market). -->
                  <input
                    class="field numeric"
                    name="countryCode"
                    maxlength="2"
                    autocomplete="country"
                    [(ngModel)]="address.countryCode"
                    (blur)="loadOptions()"
                  />
                </label>
              </div>

              <button class="button button--quiet" type="button" [disabled]="rating()" (click)="loadOptions()">
                {{ rating() ? t('checkout.loadingOptions') : t('checkout.delivery') }}
              </button>
            </fieldset>

            <!-- 2 · How -->
            @if (options().length > 0) {
              <fieldset class="step">
                <legend class="step__title">{{ t('checkout.delivery') }}</legend>

                <div class="options">
                  @for (option of options(); track option.code) {
                    <label class="option" [class.option--picked]="option.code === chosen()?.code">
                      <input
                        type="radio"
                        name="shipping"
                        [value]="option.code"
                        [checked]="option.code === chosen()?.code"
                        (change)="choose(option)"
                      />
                      <span class="option__body">
                        <span class="option__label">{{ option.label }}</span>
                        @if (option.estimatedDays) {
                          <span class="option__days">
                            {{ option.estimatedDays === 1
                                ? t('checkout.deliveryOneDay')
                                : t('checkout.deliveryDays', { days: option.estimatedDays }) }}
                          </span>
                        }
                      </span>
                      <span class="option__price numeric">{{ money(option.amount) }}</span>
                    </label>
                  }
                </div>
              </fieldset>
            }

            <!-- 3 · Pay -->
            <fieldset class="step">
              <legend class="step__title">{{ t('checkout.payment') }}</legend>
              <p class="step__hint">{{ t('checkout.paymentHint') }}</p>

              <div class="options">
                @for (card of cards; track card) {
                  <label class="option" [class.option--picked]="card === instrument()">
                    <input
                      type="radio"
                      name="instrument"
                      [value]="card"
                      [checked]="card === instrument()"
                      (change)="instrument.set(card)"
                    />
                    <span class="option__body">
                      <span class="option__label">{{ t(cardKey(card)) }}</span>
                      <span class="option__days numeric">{{ card }}</span>
                    </span>
                  </label>
                }
              </div>
            </fieldset>
          </div>

          <aside class="summary">
            @if (store.quote(); as quote) {
              <dl class="totals">
                <div class="totals__row">
                  <dt>{{ t('cart.subtotal') }}</dt>
                  <dd class="numeric">{{ money(quote.subtotal) }}</dd>
                </div>

                @if (quote.discountTotal > 0) {
                  <div class="totals__row totals__row--off">
                    <dt>{{ t('cart.discounts') }}</dt>
                    <dd class="numeric">−{{ money(quote.discountTotal) }}</dd>
                  </div>
                }

                <div class="totals__row">
                  <dt>{{ t('cart.shipping') }}</dt>
                  <dd class="numeric">
                    {{ chosen() ? money(quote.shipping) : t('cart.shippingAtCheckout') }}
                  </dd>
                </div>

                <div class="totals__row totals__row--total">
                  <dt>{{ t('cart.total') }}</dt>
                  <dd class="numeric">{{ money(quote.total) }}</dd>
                </div>

                @if (quote.taxTotal > 0) {
                  <p class="totals__tax">{{ t('cart.tax') }} · {{ money(quote.taxTotal) }}</p>
                }
              </dl>

              <button
                class="button button--block"
                type="button"
                [disabled]="paying() || !chosen()"
                (click)="pay()"
              >
                {{ paying() ? t('checkout.paying') : t('checkout.pay', { total: money(quote.total) }) }}
              </button>
            } @else {
              <p class="muted">{{ t('cart.noQuote') }}</p>
            }

            <a class="quiet" routerLink="/cart">{{ t('cart.title') }}</a>
          </aside>
        </div>
      }
    </section>
  `,
  styleUrl: './checkout-page.css',
})
export class CheckoutPage {
  protected readonly store = inject(CartStore);
  private readonly culture = inject(CultureStore);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  protected readonly cards = CARDS;

  /**
   * Prefilled with somewhere the shop actually delivers, because this is a demo
   * catalogue and making a reviewer type an address to see a checkout is a way
   * to lose them. A real shop starts empty, or from the account's book.
   */
  protected address: AddressRequest = {
    recipientName: 'Ana Ruiz',
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

  /**
   * One key per attempt at THIS basket, minted once. Pressing pay twice, a
   * proxy retrying a timeout and an agent reissuing the request are all the same
   * key, and all three must be one order.
   */
  private readonly idempotencyKey = crypto.randomUUID();

  private readonly currency = computed(() => this.store.quote()?.currency ?? 'EUR');

  constructor() {
    void this.start();
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
    if (!this.address.line1 || !this.address.city || !this.address.postalCode) return;

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
      const order = await firstValueFrom(
        this.http.post<OrderView>(
          `${this.baseUrl}/api/checkout`,
          {
            shippingAddress: this.address,
            shippingOptionCode: option.code,
            quoteHash: quote.inputHash,
            instrument: this.instrument(),
            idempotencyKey: this.idempotencyKey,
          },
          {
            headers: this.store.token ? { 'X-Cart-Token': this.store.token } : {},
            params: new HttpParams().set('culture', this.culture.active()),
          },
        ),
      );

      // The basket became an order. Clearing the token is what stops the next
      // page load asking for a cart that is gone.
      this.store.clear();

      await this.router.navigate(['/orders', order.orderId], { state: { order } });
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

  /**
   * One sentence a shopper can act on, per status code.
   *
   * The three that matter are told apart deliberately. A **402** is the card
   * saying no and the reason is the bank's, so it is quoted verbatim. A **503**
   * is the provider being unreachable, which is retryable and is not the
   * shopper's fault. A **409** is the price having moved, which is nobody's
   * fault — and it carries the new total, because "try again" without saying
   * what changed is how a checkout loses a sale it had already made.
   */
  private describe(error: unknown): string {
    const failure = error as { status?: number; error?: unknown };
    const translate = (key: string, params?: Record<string, unknown>) =>
      this.transloco.translate(key, params);

    if (failure.status === 409) {
      const changed = failure.error as PriceChangedResponse | null;
      return translate('checkout.priceChanged', { total: this.money(changed?.total ?? 0) });
    }

    const problem = failure.error as { detail?: string } | null;

    if (failure.status === 402)
      return translate('checkout.declined', { reason: problem?.detail ?? '' });

    if (failure.status === 503) return translate('checkout.unavailable');

    // 400 and 404 come back as a plain string written for a shopper — "'express'
    // is not one of the delivery options for this address" — and replacing that
    // with a generic message would throw away the useful half.
    const detail =
      typeof failure.error === 'string' && failure.error.length > 0
        ? failure.error
        : (problem?.detail ?? '');

    return translate('checkout.failed', { reason: detail });
  }
}
