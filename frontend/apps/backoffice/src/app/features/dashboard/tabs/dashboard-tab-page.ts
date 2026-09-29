import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
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
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-tab-page.html',
  styleUrl: './dashboard-tab-page.css',
})
export class DashboardTabPage {
  private readonly tab = toSignal(inject(ActivatedRoute).data.pipe(map((data) => data['tab'] as DashboardTab)), { requireSync: true });

  protected readonly period = injectDashboardPeriod();
  protected readonly layout = computed(() => DashboardLayouts[this.tab()]);
  /** On a wall, the grid shares the screen's height between its rows. */
  protected readonly wall = inject(FullScreen).active;
}
