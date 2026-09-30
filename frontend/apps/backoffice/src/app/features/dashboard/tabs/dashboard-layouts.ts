import type { DashboardTab } from './dashboard-tabs';
import type { WidgetPlacement } from '../widgets/dashboard-widgets';

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
    { widget: 'campaigns', size: 'M' },
    { widget: 'byChannel', size: 'S' },
    // Live (S) arrives with R-10.
  ],
  sales: [
    { widget: 'keyFigures', size: 'L' },
    { widget: 'salesByDay', size: 'L' },
    // Four at S in one row since D-7 added two: with the two L rows above,
    // four M would need a fourth row, and the tab would not fit its wall. At S
    // each shows its figure or its short list — what a wall is read for.
    { widget: 'byChannel', size: 'S' },
    { widget: 'bestSellers', size: 'S' },
    { widget: 'byCategory', size: 'S' },
    { widget: 'sellThrough', size: 'S' },
  ],
  operations: [
    { widget: 'ordersByStatus', size: 'M' },
    { widget: 'health', size: 'S' },
    { widget: 'funnel', size: 'S' },
    // Returns at S on Operations since search status joined the tab: on a wall
    // its three figures are what is read, and the seven widgets fit two rows.
    { widget: 'returns', size: 'S' },
    { widget: 'stock', size: 'S' },
    { widget: 'waiting', size: 'S' },
    { widget: 'searchStatus', size: 'S' },
  ],
  growth: [
    { widget: 'campaigns', size: 'M' },
    { widget: 'customers', size: 'M' },
  ],
  agents: [
    { widget: 'agentSales', size: 'M' },
    { widget: 'refusals', size: 'M' },
  ],
} as const satisfies Record<DashboardTab, readonly WidgetPlacement[]>;
