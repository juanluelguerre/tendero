import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { type BarChartData, type BarLayout, roleColourToken, type ValueFormat } from './chart-contract';
import { barOptions } from './echarts-bar-options';
import { type ChartBuild, EchartsChart } from './echarts-chart';

/**
 * A bar chart as the backoffice's widgets use it (R-9): a caption, a legend, the
 * drawing — Apache ECharts, chosen by the author over our own SVG after both were
 * measured on this chart (ADR 0045) — and a table of every figure, which is what a
 * screen reader reads and what the drawing only shows; anyone else opens it with
 * one button. ECharts loads in its own
 * chunk when the chart first reaches the screen.
 */
let charts = 0;

@Component({
  selector: 'backoffice-bar-chart',
  imports: [TranslocoDirective, EchartsChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bar-chart.html',
  host: { '[style.--chart-block-size]': 'blockSize()' },
  styleUrl: './bar-chart.css',
})
export class BarChart {
  readonly data = input.required<BarChartData>();
  readonly format = input.required<ValueFormat>();
  readonly caption = input.required<string>();
  readonly layout = input<BarLayout>('columns');

  /** Unique per chart, so the button names the table it opens. */
  protected readonly tableId = `chart-figures-${++charts}`;
  protected readonly figuresShown = signal(false);

  protected readonly build = computed<ChartBuild>(() => {
    const data = this.data();
    const format = this.format();
    const layout = this.layout();
    return (colours, reducedMotion) => barOptions(data, format, colours, reducedMotion, layout);
  });

  /** A ranking is as tall as its rows and a share is one bar; columns keep the plot's default (or a wall's). */
  protected readonly blockSize = computed(() => {
    const layout = this.layout();
    if (layout === 'share') return '3rem';
    if (layout === 'rows') return `calc(${this.data().categories.length} * 2rem + 1rem)`;
    return null;
  });

  protected colour(role: BarChartData['series'][number]['role']): string {
    return `var(${roleColourToken(role)})`;
  }
}
