import type { Provider } from '@angular/core';
import { AgentSalesWidget } from './agent-sales-widget';
import { BestSellersWidget } from './best-sellers-widget';
import { ByChannelWidget } from './by-channel-widget';
import { CustomersWidget } from './customers-widget';
import { DASHBOARD_WIDGETS, type WidgetDefinition, type WidgetKey } from './dashboard-widgets';
import { FunnelWidget } from './funnel-widget';
import { HealthWidget } from './health-widget';
import { KeyFiguresWidget } from './key-figures-widget';
import { OrdersByStatusWidget } from './orders-by-status-widget';
import { RefusalsWidget } from './refusals-widget';
import { ReturnsWidget } from './returns-widget';
import { SalesByDayWidget } from './sales-by-day-widget';
import { StockWidget } from './stock-widget';
import { TasksWidget } from './tasks-widget';

/** Which component each widget key is (R-9): the one place a new widget is registered. */
export function provideDashboardWidgets(): Provider {
  const widgets: Record<WidgetKey, WidgetDefinition> = {
    keyFigures: { component: KeyFiguresWidget, takesPeriod: true },
    salesByDay: { component: SalesByDayWidget, takesPeriod: true },
    bestSellers: { component: BestSellersWidget, takesPeriod: true },
    byChannel: { component: ByChannelWidget, takesPeriod: true },
    waiting: { component: TasksWidget, takesPeriod: false },
    health: { component: HealthWidget, takesPeriod: true },
    ordersByStatus: { component: OrdersByStatusWidget, takesPeriod: true },
    funnel: { component: FunnelWidget, takesPeriod: true },
    returns: { component: ReturnsWidget, takesPeriod: true },
    stock: { component: StockWidget, takesPeriod: false },
    customers: { component: CustomersWidget, takesPeriod: true },
    agentSales: { component: AgentSalesWidget, takesPeriod: true },
    refusals: { component: RefusalsWidget, takesPeriod: true },
  };
  return { provide: DASHBOARD_WIDGETS, useValue: widgets };
}
