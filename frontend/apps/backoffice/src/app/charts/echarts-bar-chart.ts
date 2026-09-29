import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, input } from '@angular/core';
import { readChartColours } from './chart-colours';
import type { BarChartData, ValueFormat } from './chart-contract';
import { barOptions } from './echarts-bar-options';
import { EchartsDirective } from './echarts.directive';

/** Engine 2 (R-9): the same bars through Apache ECharts, in its own deferred chunk. */
@Component({
  selector: 'backoffice-echarts-bar-chart',
  imports: [EchartsDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './echarts-bar-chart.html',
  styleUrl: './echarts-bar-chart.css',
})
export class EchartsBarChart {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly data = input.required<BarChartData>();
  readonly format = input.required<ValueFormat>();

  protected readonly options = computed(() =>
    barOptions(this.data(), this.format(), readChartColours(this.host), matchMedia('(prefers-reduced-motion: reduce)').matches),
  );
}
