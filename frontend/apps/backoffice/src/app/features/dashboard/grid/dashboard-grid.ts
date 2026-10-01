import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { DashboardPeriod } from '../../../data-access/ordering.service';
import { DASHBOARD_WIDGETS, type WidgetPlacement } from '../widgets/dashboard-widgets';
import { WidgetOutlet } from './widget-outlet';

/**
 * A dashboard drawn from its placements (R-9): which widget, at which size, in
 * which order. The layout is data and the widgets come from a registry, so a
 * tab is a list, and v2's configurable dashboard edits a list rather than a
 * template.
 */
@Component({
  selector: 'backoffice-dashboard-grid',
  imports: [WidgetOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-grid.html',
})
export class DashboardGrid {
  private readonly widgets = inject(DASHBOARD_WIDGETS);

  readonly placements = input.required<readonly WidgetPlacement[]>();
  readonly period = input.required<DashboardPeriod>();
  /** On a wall: the rows share the screen's height. */
  readonly fill = input(false);

  protected readonly cells = computed(() =>
    this.placements().map((placement) => {
      const definition = this.widgets[placement.widget];
      return {
        key: placement.widget,
        size: placement.size,
        component: definition.component,
        inputs: definition.takesPeriod ? { period: this.period() } : {},
      };
    }),
  );
}
