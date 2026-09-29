import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { CampaignView, SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { ProductImageUrls } from '@tendero/shared-util';
import { ProductCard } from '../../product-card/product-card';
import { ShopLinks } from '../../shop-links';
import { clockTarget, dayOf, holdLabels, momentOf, windowOf } from '../campaign-rules';
import { CountdownClock } from '../countdown-clock/countdown-clock';

/**
 * One campaign, as the shop leads with it (R-8, H-2; the approved mock-ups).
 *
 * - **An event** — *Tendero Days* — is the chalkboard: dark in both themes, its
 *   window, its name, its line, its clock (*Termina en…* while it runs, *Empieza
 *   en…* before), what it holds, and the view's one clay action.
 * - **A season** — *Otoño en Tendero* — is the light card, with no clock: weeks
 *   long, its end is not news.
 * - Beside the words, the campaign's own image when the shopkeeper uploaded one,
 *   or the products it reaches, so the card is never empty on one side.
 *
 * `variant="banner"` is the same card, smaller, for a category's campaign at the
 * head of its pages: no picture, no products, and a link rather than a clay
 * button, because the listing below it has the page's action.
 *
 * `campaign_card_opened` is the card's own event, fired by whatever it links to.
 */
@Component({
  selector: 'storefront-campaign-card',
  imports: [TranslocoDirective, RouterLink, CountdownClock, ProductCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './campaign-card.html',
  styleUrl: './campaign-card.css',
  host: {
    '[class.campaign--event]': "campaign().kind !== 'Season'",
    '[class.campaign--season]': "campaign().kind === 'Season'",
    '[class.campaign--banner]': "variant() === 'banner'",
    // Any of its links — the call to action or one of its products — opens it.
    '(click)': 'openedFrom($event)',
  },
})
export class CampaignCard {
  readonly campaign = input.required<CampaignView>();
  readonly variant = input<'lead' | 'banner'>('lead');
  /** What the campaign reaches, drawn when it has no image of its own (up to three). */
  readonly products = input<readonly SearchHit[]>([]);

  /** The clock reached zero: the owner asks again what leads now. */
  readonly reached = output<void>();

  private readonly culture = inject(CultureStore);
  private readonly telemetry = inject(Telemetry);
  private readonly images = inject(ProductImageUrls);

  protected readonly links = inject(ShopLinks);

  protected readonly moment = computed(() => momentOf(this.campaign()));
  protected readonly until = computed(() => clockTarget(this.campaign()));
  protected readonly window = computed(() => windowOf(this.campaign(), this.culture.active()));
  protected readonly endDay = computed(() => dayOf(this.campaign().endsAt, this.culture.active()));
  protected readonly startDay = computed(() => dayOf(this.campaign().startsAt, this.culture.active()));
  protected readonly image = computed(() => {
    const id = this.campaign().imageId;
    return id && this.variant() === 'lead' ? this.images.of(id) : null;
  });
  protected readonly shownProducts = computed(() => (this.variant() === 'lead' ? this.products().slice(0, 3) : []));

  protected holds(t: (key: string, params?: Record<string, unknown>) => string): string[] {
    return holdLabels(this.campaign(), (name, code) => t('campaign.holdWithCode', { name, code }));
  }

  /** The listing of what the campaign reaches, headed with its name (`?campaign=`). */
  protected readonly listing = computed(() => ({ campaign: this.campaign().code }));

  /** A link of the card followed, not a click anywhere on it. */
  protected openedFrom(event: Event): void {
    if (!(event.target instanceof Element) || !event.target.closest('a')) return;
    this.telemetry.trackEvent('campaign_card_opened', { campaign: this.campaign().code, phase: this.moment() });
  }
}
