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
      <header class="head">
        <h1>{{ t('promotions.title') }}</h1>
        @if (running() > 0) {
          <span class="tag tag--ok">{{ t('promotions.running', { count: running() }) }}</span>
        }
      </header>
      <p class="hint">{{ t('promotions.hint') }}</p>

      @if (loading()) {
        <p class="hint" role="status" aria-live="polite">{{ t('promotions.loading') }}</p>
      } @else if (failed()) {
        <p class="failed" role="alert">{{ t('promotions.failed') }}</p>
      } @else if (items().length === 0) {
        <p class="hint">{{ t('promotions.empty') }}</p>
      } @else {
        <table>
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
                <td class="numeric">{{ promotion.code }}</td>
                <td>
                  {{ name(promotion) }}
                  @if (promotion.missingCultures.length > 0) {
                    <span class="tag tag--warn">{{ promotion.missingCultures.join(' · ') }}</span>
                  }
                </td>
                <td>
                  <span class="hint">{{ promotion.effect }}</span>
                  <span class="numeric">{{ promotion.effectDetail }}</span>
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
      }
    </section>
  `,
  styles: `
    .head { display: flex; align-items: baseline; gap: var(--space-3); }
    h1 { font-family: var(--font-display); font-size: var(--text-lg); margin: 0; }
    .hint { color: var(--text-muted); font-size: var(--text-xs); }
    .failed { color: var(--danger); font-size: var(--text-sm); }
    table { width: 100%; border-collapse: collapse; font-size: var(--text-xs); }
    th, td { text-align: start; padding: var(--space-2); border-block-end: 1px solid var(--border); vertical-align: top; }
    th { color: var(--text-muted); font-weight: 500; }
    .policy { display: inline-block; font-size: var(--text-2xs); padding: 0 var(--space-2); border-radius: var(--radius-sm); border: 1px solid var(--border); }
    .policy--ExclusiveGlobal { border-color: var(--danger); color: var(--danger); }
    .policy--ExclusiveInGroup { border-color: var(--warning); color: var(--warning); }
    .policy--Stackable { border-color: var(--positive); color: var(--positive); }
    .group { display: inline-block; margin-inline-start: var(--space-2); font-family: var(--font-mono); font-size: var(--text-2xs); color: var(--text-muted); }
    .tag { display: inline-block; margin-inline-start: var(--space-2); padding: 0 var(--space-2); border-radius: var(--radius-sm); font-size: var(--text-2xs); }
    .tag--ok { background: color-mix(in srgb, var(--positive) 18%, transparent); color: var(--positive); }
    .tag--warn { background: color-mix(in srgb, var(--warning) 18%, transparent); color: var(--warning); }
    .tag--code { background: color-mix(in srgb, var(--text-muted) 18%, transparent); color: var(--text-muted); font-family: var(--font-mono); }
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
