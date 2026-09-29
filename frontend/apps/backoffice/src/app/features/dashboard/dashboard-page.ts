import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { AgentsWidget } from './agents-widget';
import { FunnelWidget } from './funnel-widget';
import { OperationsWidget } from './operations-widget';
import { SalesByDayWidget } from './sales-by-day-widget';
import { SalesWidget } from './sales-widget';
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
  imports: [TranslocoDirective, SalesWidget, SalesByDayWidget, OperationsWidget, FunnelWidget, AgentsWidget, TasksWidget],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.css',
})
export class DashboardPage {
  protected readonly periods: readonly DashboardPeriod[] = ['Day', 'Week', 'Month'];

  protected readonly period = signal<DashboardPeriod>('Week');
}
