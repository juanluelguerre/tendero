import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { type DashboardPeriod, OrderingService } from '../../data-access/ordering.service';
import { formatShare } from './figures';

/**
 * The work in hand and the shop's health (P11-7): orders to ship and the late ones,
 * payments stuck or failed, cancellations, and returns — open by reason, refunded,
 * and as a rate. Seller Central calls the second half *Account Health*; here it is
 * the half a shopkeeper acts on before lunch.
 */
@Component({
  selector: 'backoffice-operations-widget',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './operations-widget.html',
  styleUrl: './operations-widget.css',
})
export class OperationsWidget {
  private readonly ordering = inject(OrderingService);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected readonly operations = rxResource({
    params: () => ({ period: this.period() }),
    stream: ({ params }) => this.ordering.operations(params.period),
  });

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  /** The server labels a reason `wrong_size`; the returns screen's words are keyed `WrongSize`. */
  protected reasonKey(label: string): string {
    return 'returns.reason.' + label.replace(/(^|_)([a-z])/g, (_, __, letter: string) => letter.toUpperCase());
  }

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }
}
