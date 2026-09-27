import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { type DashboardPeriod, OrderingService } from '../../data-access/ordering.service';
import { type Change, changeOf, formatChange } from './figures';

/**
 * What sold (P11-7): revenue, orders, units and the average order against the
 * previous period, and the five best sellers — the first thing Shopify's home and
 * Seller Central's both show, because it is the first thing a shopkeeper asks.
 */
@Component({
  selector: 'backoffice-sales-widget',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sales-widget.html',
  styleUrl: './sales-widget.css',
})
export class SalesWidget {
  private readonly ordering = inject(OrderingService);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected readonly sales = rxResource({
    params: () => ({ period: this.period() }),
    stream: ({ params }) => this.ordering.sales(params.period),
  });

  protected readonly figures = computed(() => {
    const sales = this.sales.value();
    if (!sales) return [];
    const { current, previous } = sales;
    return [
      { key: 'revenue', value: this.money(current.revenue), change: changeOf(current.revenue, previous.revenue) },
      { key: 'orders', value: String(current.orders), change: changeOf(current.orders, previous.orders) },
      { key: 'units', value: String(current.units), change: changeOf(current.units, previous.units) },
      { key: 'averageOrder', value: this.money(current.averageOrderValue), change: changeOf(current.averageOrderValue, previous.averageOrderValue) },
    ];
  });

  protected money(amount: number): string {
    return formatPrice(amount, this.sales.value()?.currency ?? 'EUR', this.culture.active());
  }

  protected changeText(change: Change): string | null {
    return formatChange(change, this.culture.active());
  }
}
