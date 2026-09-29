import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { type BarChartData, roleColourToken, type ValueFormat } from './chart-contract';
import { niceMax, ticks } from './scale';

/**
 * Engine 1 (R-9): bars as SVG rectangles in a 100 × 100 box that stretches with
 * its widget — proportional by construction, never pixels — and the labels in
 * HTML under it, so no text is stretched. No dependency.
 */
@Component({
  selector: 'backoffice-svg-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './svg-bar-chart.html',
  styleUrl: './svg-bar-chart.css',
})
export class SvgBarChart {
  readonly data = input.required<BarChartData>();
  readonly format = input.required<ValueFormat>();

  protected readonly max = computed(() => niceMax(Math.max(0, ...this.data().series.flatMap((series) => series.values))));
  protected readonly gridlines = computed(() => ticks(this.max()).slice(1).map((tick) => 100 - (tick / this.max()) * 100));

  protected readonly bars = computed(() => {
    const { categories, series } = this.data();
    const groupWidth = 100 / categories.length;
    const barWidth = (groupWidth * 0.7) / series.length;
    return categories.flatMap((category, index) =>
      series.map((one, position) => {
        const value = one.values[index] ?? 0;
        const height = Math.round((value / this.max()) * 100);
        return {
          key: one.key,
          value,
          x: index * groupWidth + groupWidth * 0.15 + position * barWidth,
          y: 100 - height,
          width: barWidth * 0.92,
          height,
          colour: `var(${roleColourToken(one.role)})`,
          title: `${category} · ${one.label} · ${this.format()(value)}`,
        };
      }),
    );
  });
}
