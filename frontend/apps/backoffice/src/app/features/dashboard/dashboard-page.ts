import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { injectDashboardPeriod, queryOfPeriod } from './dashboard-period';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { type DashboardTab, DashboardTabs } from './dashboard-tabs';
import { provideDashboardWidgets } from './dashboard-widget-registry';

/**
 * The backoffice's home (P11-7), since R-9 an Overview and four tabs (spec,
 * option B): the shop's pulse for the shopkeeper, read from Postgres — never
 * from Prometheus, because the observability stack is a switch and a
 * shopkeeper's home cannot be.
 *
 * The page is the frame: the tabs, each with its URL, and one period for all of
 * them, in the URL too (`?period=week`), so a monitor keeps both. Comparing a
 * week's sales with a day's refusals is a mistake the interface should not make
 * easy. What each tab shows is data (`DashboardLayouts`), drawn by the grid from
 * the widgets registered here; the reads they share live and die with the page.
 */
@Component({
  selector: 'backoffice-dashboard',
  imports: [TranslocoDirective, RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.css',
  providers: [DashboardSources, DashboardRefresh, provideDashboardWidgets()],
})
export class DashboardPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly periods: readonly DashboardPeriod[] = ['Day', 'Week', 'Month'];
  protected readonly tabs = DashboardTabs;
  protected readonly period = injectDashboardPeriod();
  protected readonly queryOfPeriod = queryOfPeriod;

  protected linkOf(tab: DashboardTab): string[] {
    return tab === 'overview' ? ['/dashboard'] : ['/dashboard', tab];
  }

  protected choose(period: DashboardPeriod): void {
    void this.router.navigate([], { relativeTo: this.route.firstChild ?? this.route, queryParams: { period: queryOfPeriod(period) }, queryParamsHandling: 'merge' });
  }
}
