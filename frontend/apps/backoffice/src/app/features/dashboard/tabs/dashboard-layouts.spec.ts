import { describe, expect, it } from 'vitest';
import { DashboardLayouts, DashboardTabs } from './dashboard-layouts';

const Columns = { S: 3, M: 6, L: 12 } as const;

describe('the dashboards as data', () => {
  it('has the five tabs, the Overview first', () => {
    expect(DashboardTabs).toEqual(['overview', 'sales', 'operations', 'growth', 'agents']);
  });

  it('opens the Overview with the key figures across the whole width', () => {
    expect(DashboardLayouts.overview[0]).toEqual({ widget: 'keyFigures', size: 'L' });
  });

  it('places no widget twice on one tab', () => {
    for (const tab of DashboardTabs) {
      const widgets = DashboardLayouts[tab].map((placement) => placement.widget);
      expect(new Set(widgets).size, tab).toBe(widgets.length);
    }
  });

  /** The Overview "fits one screen at 1440" (spec): the rows it starts are whole rows of twelve. */
  it('fills the Overview rows it starts', () => {
    const columns = DashboardLayouts.overview.map((placement) => Columns[placement.size]);
    expect(columns.slice(0, 4).reduce((sum, width) => sum + width, 0)).toBe(24);
  });
});
