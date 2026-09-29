import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { refreshedResource } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { bySkuLowestFirst } from './figures';

const Shown = 5;

/**
 * The stock (spec, Operations): how many SKUs to restock and how many are sold
 * out, as Inventory counts them by its own rule, and from M the lowest first.
 * Stock is now, not a window, so this widget takes no period. Coverage in days
 * arrives with Plan 3's data.
 */
@Component({
  selector: 'backoffice-stock-widget',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stock-widget.html',
  styleUrl: './stock-widget.css',
})
export class StockWidget {
  private readonly sources = inject(DashboardSources);
  private readonly culture = inject(CultureStore);

  protected readonly stock = refreshedResource({
    params: () => ({ culture: this.culture.active() }),
    stream: (params, at) => this.sources.stock(params.culture, at),
  });

  private readonly counts = computed(() => this.stock.value()?.counts ?? []);
  protected readonly restock = computed(() => this.counts().find((count) => count.filter === 'Restock')?.count ?? 0);
  protected readonly soldOut = computed(() => this.counts().find((count) => count.filter === 'SoldOut')?.count ?? 0);
  protected readonly lowest = computed(() => bySkuLowestFirst(this.stock.value()?.rows ?? []).slice(0, Shown));
}
