import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { CHART_ENGINE, type BarChartData, roleColourToken, type ValueFormat } from './chart-contract';
import { EchartsBarChart } from './echarts-bar-chart';
import { SvgBarChart } from './svg-bar-chart';

/**
 * A bar chart as the backoffice's widgets use it (R-9): a caption, a legend, the
 * drawing — by whichever engine `CHART_ENGINE` names — and a table of every
 * figure, which is what a screen reader reads and what the drawing only shows.
 * ECharts is loaded only when it is the engine, in its own chunk.
 */
@Component({
  selector: 'backoffice-bar-chart',
  imports: [SvgBarChart, EchartsBarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bar-chart.html',
  styleUrl: './bar-chart.css',
})
export class BarChart {
  protected readonly engine = inject(CHART_ENGINE);

  readonly data = input.required<BarChartData>();
  readonly format = input.required<ValueFormat>();
  readonly caption = input.required<string>();

  protected colour(role: BarChartData['series'][number]['role']): string {
    return `var(${roleColourToken(role)})`;
  }
}
