/**
 * The dashboard's tabs (spec, option B), on their own and importing nothing, so
 * the app's routes can name them without pulling a widget into the first bundle.
 */
export type DashboardTab = 'overview' | 'sales' | 'operations' | 'growth' | 'agents';

/** In the nav's order: the Overview first. */
export const DashboardTabs: readonly DashboardTab[] = ['overview', 'sales', 'operations', 'growth', 'agents'];

/** A tab's path under /dashboard: the Overview is /dashboard itself. */
export function pathOfTab(tab: DashboardTab): string {
  return tab === 'overview' ? '' : tab;
}
