import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { PromotionView } from '@tendero/shared-api';
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
  template: `
    <section *transloco="let t">
      <header class="page-head">
        <h1 class="page-title">{{ t('promotions.title') }}</h1>
        @if (running() > 0) {
          <span class="tag tag--ok">{{ t('promotions.running', { count: running() }) }}</span>
        }
      </header>
      <p class="page-hint">{{ t('promotions.hint') }}</p>

      @if (loading()) {
        <p class="muted" role="status" aria-live="polite">{{ t('promotions.loading') }}</p>
      } @else if (failed()) {
        <p class="failed" role="alert">{{ t('promotions.failed') }}</p>
      } @else if (items().length === 0) {
        <div class="empty"><p class="empty__text">{{ t('promotions.empty') }}</p></div>
      } @else {
        <div class="table-wrap">
        <table class="table">
          <caption class="sr-only">{{ t('promotions.title') }}</caption>
          <thead>
            <tr>
              <th scope="col">{{ t('promotions.column.code') }}</th>
              <th scope="col">{{ t('promotions.column.name') }}</th>
              <th scope="col">{{ t('promotions.column.effect') }}</th>
              <th scope="col">{{ t('promotions.column.combination') }}</th>
              <th scope="col" class="numeric">{{ t('promotions.column.priority') }}</th>
              <th scope="col">{{ t('promotions.column.state') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (promotion of items(); track promotion.code) {
              <tr>
                <td class="numeric code">{{ promotion.code }}</td>
                <td>
                  {{ name(promotion) }}
                  @if (promotion.missingCultures.length > 0) {
                    <span class="tag tag--warn">{{ promotion.missingCultures.join(' · ') }}</span>
                  }
                </td>
                <td>
                  <span class="effect">{{ promotion.effect }}</span>
                  <span class="numeric effect__detail">{{ promotion.effectDetail }}</span>
                </td>
                <!-- The load-bearing column. A group name beside the policy is
                     what turns "ExclusiveInGroup" into something actionable:
                     it names who this one would suppress. -->
                <td>
                  <span class="policy" [class]="'policy--' + promotion.combination">
                    {{ t('promotions.policy.' + promotion.combination) }}
                  </span>
                  @if (promotion.exclusivityGroup) {
                    <span class="group">{{ promotion.exclusivityGroup }}</span>
                  }
                </td>
                <td class="numeric">{{ promotion.priority }}</td>
                <td>
                  @if (promotion.isActive) {
                    <span class="tag tag--ok">{{ t('promotions.state.running') }}</span>
                  } @else {
                    <span class="tag tag--warn">{{ t('promotions.state.scheduled') }}</span>
                  }
                  @if (promotion.couponCode) {
                    <span class="tag tag--code">{{ t('promotions.needsCoupon') }}</span>
                  }
                  @if (promotion.segment) {
                    <span class="tag tag--code">{{ promotion.segment }}</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
        </div>
      }
    </section>
  `,
  styles: `
    /* Only what this page adds; the rest is a primitive in styles.css. */
    .code { color: var(--text); font-weight: 500; }

    /* The effect reads as one value: kind then amount, with a space that the
       two spans did not have — "percent-off-line30%" was the first thing the
       screen got wrong when it was seen running. */
    .effect { color: var(--text-muted); }
    .effect__detail { margin-inline-start: var(--space-2); color: var(--text); }

    /* The load-bearing column. Each policy gets its own outline so the shape of
       the table answers "what stops applying?" before anything is read. */
    .policy {
      display: inline-block;
      padding: 1px var(--space-2);
      border: 1px solid currentColor;
      border-radius: var(--radius-sm);
      font-size: var(--text-3xs);
      white-space: nowrap;
    }
    .policy--ExclusiveGlobal { color: var(--danger); }
    .policy--ExclusiveInGroup { color: var(--warning); }
    .policy--Stackable { color: var(--positive); }

    .group {
      display: inline-block;
      margin-inline-start: var(--space-2);
      font-family: var(--font-mono);
      font-size: var(--text-3xs);
      color: var(--text-subtle);
    }
  `,
})
export class PromotionsPage {
  private readonly pricing = inject(PricingService);

  protected readonly items = signal<PromotionView[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected running(): number {
    return this.items().filter((promotion) => promotion.isActive).length;
  }

  /**
   * Spanish with an English fallback, unlike the attributes screen. Here the
   * name is context for the rule rather than the thing under review, and an
   * empty cell would say less than the other language does — the missing
   * culture is still reported, by its own tag.
   */
  protected name(promotion: PromotionView): string {
    return promotion.name['es'] ?? promotion.name['en'] ?? promotion.code;
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
