import type { DashboardTab } from './dashboard-tabs';
import type { WidgetPlacement } from './dashboard-widgets';

export { type DashboardTab, DashboardTabs, pathOfTab } from './dashboard-tabs';

/**
 * Each tab as the list of its widgets and their sizes (spec, the Overview and
 * the tabs). Only what today's data can show: a widget whose data Plan 3 brings
 * is absent, not faked — and the comment says where it will go.
 */
export const DashboardLayouts = {
  overview: [
    { widget: 'keyFigures', size: 'L' },
    { widget: 'salesByDay', size: 'M' },
    { widget: 'waiting', size: 'S' },
    { widget: 'health', size: 'S' },
    // Campaigns (M) arrives with its baseline in Plan 3, and Live (S) with R-10.
    { widget: 'byChannel', size: 'S' },
  ],
  sales: [
    { widget: 'keyFigures', size: 'L' },
    { widget: 'salesByDay', size: 'L' },
    { widget: 'byChannel', size: 'M' },
    { widget: 'bestSellers', size: 'M' },
    // By category arrives with the category on the order line, in Plan 3.
  ],
  operations: [
    { widget: 'ordersByStatus', size: 'M' },
    { widget: 'health', size: 'S' },
    { widget: 'funnel', size: 'S' },
    { widget: 'returns', size: 'M' },
    { widget: 'stock', size: 'S' },
    { widget: 'waiting', size: 'S' },
  ],
  growth: [
    // Campaigns against their baseline and Search's status arrive in Plan 3.
    { widget: 'customers', size: 'M' },
  ],
  agents: [
    { widget: 'agentSales', size: 'M' },
    { widget: 'refusals', size: 'M' },
  ],
} as const satisfies Record<DashboardTab, readonly WidgetPlacement[]>;
