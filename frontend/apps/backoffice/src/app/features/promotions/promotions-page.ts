import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { PromotionView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { PricingService } from '../../data-access/pricing.service';

/**
 * The promotions, with the combination column that is the point of the screen.
 *
 * A list showing code, dates and amount is a list of discounts. The question a
 * shopkeeper actually has is "if I turn this on, what stops applying?", and that
 * is answered by the policy and the exclusivity group — so those are the two
 * columns that get the room, and rivals in a group are shown together.
 *
 * Read-only, and said out loud rather than quietly: promotions come from a
 * committed file today. Editing them needs somewhere to write, and the table
 * created for the attribute definitions in phase 2 was dropped the next day for
 * having no reader. It comes back when editing is the feature.
 */
@Component({
  selector: 'backoffice-promotions',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './promotions-page.html',
  styleUrl: './promotions-page.css',
})
export class PromotionsPage {
  private readonly pricing = inject(PricingService);
  private readonly culture = inject(CultureStore);

  protected readonly items = signal<PromotionView[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected running(): number {
    return this.items().filter((promotion) => promotion.isActive).length;
  }

  /**
   * The name in the active culture, with the same fallback chain the server's
   * LocalizedText uses: requested -> en -> the code.
   *
   * It was hardcoded to Spanish, which was invisible while both apps were
   * permanently Spanish and became a bug the moment there was a switcher: an
   * English backoffice listed "Rebajas de verano". The missing culture is still
   * reported by its own tag, so falling back here hides nothing.
   */
  protected name(promotion: PromotionView): string {
    return promotion.name[this.culture.active()] ?? promotion.name['en'] ?? promotion.code;
  }

  constructor() {
    this.pricing.promotions().subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }
}
