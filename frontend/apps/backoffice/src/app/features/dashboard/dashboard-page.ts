import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { AgentsWidget } from './agents-widget';
import { BestSellersWidget } from './best-sellers-widget';
import { ByChannelWidget } from './by-channel-widget';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { FunnelWidget } from './funnel-widget';
import { HealthWidget } from './health-widget';
import { KeyFiguresWidget } from './key-figures-widget';
import { OrdersByStatusWidget } from './orders-by-status-widget';
import { ReturnsWidget } from './returns-widget';
import { SalesByDayWidget } from './sales-by-day-widget';
import { StockWidget } from './stock-widget';
import { TasksWidget } from './tasks-widget';

/**
 * The backoffice's home (P11-7): the shop's pulse for the shopkeeper, read from
 * Postgres — never from Prometheus, because the observability stack is a switch
 * and a shopkeeper's home cannot be. Grafana's *Shop pulse* stays the developer's.
 *
 * **Widgets from the first version, in a fixed layout.** Each figure is a
 * component asking its own typed endpoint, so a widget can be moved, hidden or
 * added without touching the others; rearranging and hiding per user and role is
 * v2, a dashboard builder v3 (the standing backlog). One period for the page,
 * because comparing a week's sales with a day's refusals is a mistake the
 * interface should not make easy.
 */
@Component({
  selector: 'backoffice-dashboard',
  imports: [
    TranslocoDirective, KeyFiguresWidget, SalesByDayWidget, BestSellersWidget, ByChannelWidget,
    OrdersByStatusWidget, HealthWidget, ReturnsWidget, StockWidget, FunnelWidget, AgentsWidget, TasksWidget,
  ],
  providers: [DashboardSources, DashboardRefresh],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.css',
})
export class DashboardPage {
  protected readonly periods: readonly DashboardPeriod[] = ['Day', 'Week', 'Month'];

  protected readonly period = signal<DashboardPeriod>('Week');
}
