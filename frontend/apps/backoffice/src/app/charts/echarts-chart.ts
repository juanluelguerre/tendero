import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, input } from '@angular/core';
import { ThemeStore } from '@tendero/shared-ui';
import type { EChartsCoreOption } from 'echarts/core';
import { type ChartColours, readChartColours } from './chart-colours';
import { EchartsDirective } from './echarts.directive';

/** What a chart component hands the host: its options, given the tokens' colours and the reader's motion preference. */
export type ChartBuild = (colours: ChartColours, reducedMotion: boolean) => EChartsCoreOption;

/**
 * Every ECharts chart in the backoffice, whatever it draws (R-9): the tokens'
 * colours read where the chart sits, read again when the theme changes, and the
 * reader's motion preference — once, here, rather than in each chart. Loaded in
 * the deferred chunk with ECharts itself.
 */
@Component({
  selector: 'backoffice-echarts-chart',
  imports: [EchartsDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './echarts-chart.html',
  // A block; its plot's height is the chart's to set.
  host: { class: 'block' },
})
export class EchartsChart {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly theme = inject(ThemeStore);

  readonly build = input.required<ChartBuild>();

  /** Read after the theme so a switch rebuilds: the tokens change under the same element. */
  protected readonly options = computed(() => {
    this.theme.applied();
    return this.build()(readChartColours(this.host), matchMedia('(prefers-reduced-motion: reduce)').matches);
  });
}
