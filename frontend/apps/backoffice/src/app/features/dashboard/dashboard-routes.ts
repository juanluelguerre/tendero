import type { Routes } from '@angular/router';
import { DashboardTabs, pathOfTab } from './dashboard-tabs';

/**
 * One route per tab under /dashboard (spec: each tab has its URL, so each can
 * sit on a monitor of its own). The same page for all five, told apart by its
 * data; the app's routes and the page's spec share this list.
 */
export const dashboardChildren: Routes = DashboardTabs.map((tab) => ({
  path: pathOfTab(tab),
  pathMatch: 'full' as const,
  title: `dashboard.tabTitles.${tab}`,
  data: { tab },
  loadComponent: () => import('./dashboard-tab-page').then((m) => m.DashboardTabPage),
}));
