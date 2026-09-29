import type { EChartsCoreOption } from 'echarts/core';
import type { BarChartData, SeriesRole, ValueFormat } from './chart-contract';

/** A bar chart's options, as a pure function of the contract's data: tested without a canvas. */
export function barOptions(
  data: BarChartData,
  format: ValueFormat,
  colours: Readonly<Record<SeriesRole, string>>,
  reducedMotion: boolean,
): EChartsCoreOption {
  return {
    animation: !reducedMotion,
    aria: { enabled: true },
    grid: { left: 8, right: 8, top: 8, bottom: 24, containLabel: true },
    tooltip: { trigger: 'axis', valueFormatter: (value: unknown) => format(Number(value)) },
    xAxis: { type: 'category', data: [...data.categories], axisTick: { show: false } },
    yAxis: { type: 'value', axisLabel: { formatter: (value: number) => format(value) }, splitNumber: 4 },
    series: data.series.map((series) => ({
      name: series.label,
      type: 'bar',
      data: [...series.values],
      barGap: '8%',
      itemStyle: { color: colours[series.role], borderRadius: [4, 4, 0, 0] },
    })),
  };
}
