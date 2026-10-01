import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { DashboardGrid } from '../grid/dashboard-grid';
import { DashboardLayouts } from './dashboard-layouts';
import { injectDashboardPeriod } from '../dashboard-period';
import type { DashboardTab } from './dashboard-tabs';
import { FullScreen } from '../full-screen';

/** One tab of the dashboard (R-9): its list of widgets, drawn by the grid on the URL's period. */
@Component({
  selector: 'backoffice-dashboard-tab',
  imports: [DashboardGrid],
  templateUrl: './dashboard-tab-page.html',
  // On the wall the page's section is a column and this tab takes what the head leaves
  // (spec: the tab fills the monitor). Here, on the tab's own host: the router creates
  // this element from code, so the page's stylesheet cannot reach it — its rule for it
  // never matched (the whole-branch review).
  host: { class: 'block flex-1 min-h-0' },
})
export class DashboardTabPage {
  private readonly tab = toSignal(inject(ActivatedRoute).data.pipe(map((data) => data['tab'] as DashboardTab)), {
    requireSync: true,
  });

  protected readonly period = injectDashboardPeriod();
  protected readonly layout = computed(() => DashboardLayouts[this.tab()]);
  /** On a wall, the grid shares the screen's height between its rows. */
  protected readonly wall = inject(FullScreen).active;
}
