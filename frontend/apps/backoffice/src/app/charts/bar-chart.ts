import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { type BarChartData, roleColourToken, type ValueFormat } from './chart-contract';
import { EchartsBarChart } from './echarts-bar-chart';

/**
 * A bar chart as the backoffice's widgets use it (R-9): a caption, a legend, the
 * drawing — Apache ECharts, chosen by the author over our own SVG after both were
 * measured on this chart (ADR 0045) — and a table of every figure, which is what a
 * screen reader reads and what the drawing only shows. ECharts loads in its own
 * chunk when the chart first reaches the screen.
 */
@Component({
  selector: 'backoffice-bar-chart',
  imports: [EchartsBarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bar-chart.html',
  styleUrl: './bar-chart.css',
})
export class BarChart {
  readonly data = input.required<BarChartData>();
  readonly format = input.required<ValueFormat>();
  readonly caption = input.required<string>();

  protected colour(role: BarChartData['series'][number]['role']): string {
    return `var(${roleColourToken(role)})`;
  }
}
