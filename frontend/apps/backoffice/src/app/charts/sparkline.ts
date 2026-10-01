import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { type ChartBuild, EchartsChart } from './echarts-chart';
import { sparklineOptions } from './echarts-sparkline-options';

/**
 * A trend beside a key figure (R-9). Hidden from a screen reader on purpose: the
 * figure and its change beside it are what it says, and the day-by-day values are
 * Sales by day's table.
 */
@Component({
  selector: 'backoffice-sparkline',
  imports: [EchartsChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sparkline.html',
  // A sparkline's height, which its plot reads; the placeholder is in the template.
  host: { class: 'block [--chart-block-size:2.5rem]', 'aria-hidden': 'true' },
})
export class Sparkline {
  readonly values = input.required<readonly number[]>();

  protected readonly build = computed<ChartBuild>(() => {
    const values = this.values();
    return (colours, reducedMotion) => sparklineOptions(values, colours, reducedMotion);
  });
}
