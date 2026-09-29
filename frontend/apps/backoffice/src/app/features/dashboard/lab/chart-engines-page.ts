import { ApplicationRef, ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { BarChart } from '../../../charts/bar-chart';
import { type BarChartData, CHART_ENGINE } from '../../../charts/chart-contract';

/**
 * One engine per box. The chart is declared in the box's own template, so the box's
 * provider is its injector parent — projected content would keep the page's injector
 * and draw both with the default engine. A one-line template is the documented
 * exception to the three-files rule: the box has no markup or style of its own; if
 * lint rejects it, give each box its own `.html`.
 */
@Component({
  selector: 'backoffice-svg-engine-box',
  imports: [BarChart],
  providers: [{ provide: CHART_ENGINE, useValue: 'svg' }],
  template: '<backoffice-bar-chart [data]="data()" [format]="format()" [caption]="caption()" />',
})
export class SvgEngineBox {
  readonly data = input.required<BarChartData>();
  readonly format = input.required<(value: number) => string>();
  readonly caption = input.required<string>();
}

@Component({
  selector: 'backoffice-echarts-engine-box',
  imports: [BarChart],
  providers: [{ provide: CHART_ENGINE, useValue: 'echarts' }],
  template: '<backoffice-bar-chart [data]="data()" [format]="format()" [caption]="caption()" />',
})
export class EchartsEngineBox {
  readonly data = input.required<BarChartData>();
  readonly format = input.required<(value: number) => string>();
  readonly caption = input.required<string>();
}

const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

function randomWeek(): BarChartData {
  const values = () => DAYS.map(() => Math.round(200 + Math.random() * 800));
  return {
    categories: DAYS,
    series: [
      { key: 'current', label: 'This week', role: 'data', values: values() },
      { key: 'previous', label: 'Week before', role: 'previous', values: values() },
    ],
  };
}

/**
 * R-9's comparison, not a screen of the shop: the same kind of week drawn by each
 * engine, and the work each does to redraw it. Each engine is measured on its
 * own — only its data changes — and what is timed is the change being applied
 * (`ApplicationRef.tick()`, which runs the effects, ECharts' `setOption`
 * included), not frames: waiting a frame per redraw measured the screen's refresh
 * rate, fifty frames at 60 Hz, and nothing about either engine. Removed with the
 * engine that loses (ADR 0045).
 */
@Component({
  selector: 'backoffice-chart-engines-page',
  imports: [TranslocoDirective, SvgEngineBox, EchartsEngineBox],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './chart-engines-page.html',
  styleUrl: './chart-engines-page.css',
})
export class ChartEnginesPage {
  private readonly app = inject(ApplicationRef);

  protected readonly svgData = signal<BarChartData>(randomWeek());
  protected readonly echartsData = signal<BarChartData>(randomWeek());
  protected readonly format = (value: number) => `€${value}`;
  protected readonly timings = signal<Readonly<Record<'svg' | 'echarts', string | null>>>({ svg: null, echarts: null });

  protected redraw(engine: 'svg' | 'echarts', times = 50): void {
    const data = engine === 'svg' ? this.svgData : this.echartsData;
    let work = 0;
    for (let index = 0; index < times; index++) {
      data.set(randomWeek());
      const started = performance.now();
      this.app.tick();
      work += performance.now() - started;
    }
    const label = engine === 'svg' ? 'SVG' : 'ECharts';
    this.timings.update((current) => ({ ...current, [engine]: `${label}: ${times} redraws, ${work.toFixed(1)} ms of work (${(work / times).toFixed(2)} ms each)` }));
  }

  protected readonly caption = computed(() => 'Sales by day — the same week, two engines');
}
