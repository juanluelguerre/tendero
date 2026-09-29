import { afterNextRender, DestroyRef, Directive, effect, ElementRef, inject, input } from '@angular/core';
import { BarChart } from 'echarts/charts';
import { AriaComponent, GridComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';

echarts.use([BarChart, GridComponent, TooltipComponent, AriaComponent, SVGRenderer]);

/**
 * ECharts' binding to Angular, ours (ADR 0045: no `ngx-echarts`): created after
 * the first render, given new options when its signal changes, resized with its
 * element, disposed with it.
 */
@Directive({ selector: '[backofficeEcharts]' })
export class EchartsDirective {
  readonly options = input.required<echarts.EChartsCoreOption>({ alias: 'backofficeEcharts' });

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly destroyRef = inject(DestroyRef);
  private chart: echarts.ECharts | undefined;

  constructor() {
    afterNextRender(() => {
      this.chart = echarts.init(this.element, null, { renderer: 'svg' });
      this.chart.setOption(this.options(), { notMerge: true });
      const resize = new ResizeObserver(() => this.chart?.resize());
      resize.observe(this.element);
      this.destroyRef.onDestroy(() => {
        resize.disconnect();
        this.chart?.dispose();
      });
    });

    effect(() => {
      const options = this.options();
      this.chart?.setOption(options, { notMerge: true });
    });
  }
}
