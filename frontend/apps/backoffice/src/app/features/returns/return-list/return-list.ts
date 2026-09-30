import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, orderReference } from '@tendero/shared-util';
import { FilterChips } from '../../../worklist/filter-chips/filter-chips';
import { nextDecision } from '../return-decisions';
import { ReturnStatusLabel } from '../return-status-label/return-status-label';
import { ReturnsStore } from '../returns.store';

/**
 * The returns, narrowed by status (R-4.4): which of an order's returns, what
 * comes back and why, the refund, and the likely decision kept on the row. It
 * reads and acts through the page's store, which the pane shares.
 */
@Component({
  selector: 'backoffice-return-list',
  imports: [TranslocoDirective, NgTemplateOutlet, FilterChips, ReturnStatusLabel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './return-list.html',
  styleUrl: './return-list.css',
})
export class ReturnList {
  protected readonly store = inject(ReturnsStore);
  private readonly culture = inject(CultureStore);

  protected readonly nextDecision = nextDecision;

  /** A full Guid is unreadable; its random end is what a person reads, and what finds it. */
  protected readonly shortId = orderReference;

  protected money(amount: number): string {
    return formatPrice(amount, 'EUR', this.culture.active());
  }
}
