import type { EChartsCoreOption } from 'echarts/core';
import type { ChartColours } from './chart-colours';
import type { BarChartData, ValueFormat } from './chart-contract';

/** A bar chart's options, as a pure function of the contract's data: tested without a canvas. */
export function barOptions(data: BarChartData, format: ValueFormat, colours: ChartColours, reducedMotion: boolean): EChartsCoreOption {
  const rule = { lineStyle: { color: colours.border } };
  return {
    animation: !reducedMotion,
    aria: { enabled: true },
    textStyle: { color: colours.text, fontFamily: colours.font },
    grid: { left: 8, right: 8, top: 8, bottom: 24, containLabel: true },
    tooltip: {
      trigger: 'axis',
      valueFormatter: (value: unknown) => format(Number(value)),
      backgroundColor: colours.surface,
      borderColor: colours.border,
      textStyle: { color: colours.text },
    },
    xAxis: {
      type: 'category',
      data: [...data.categories],
      axisTick: { show: false },
      axisLabel: { color: colours.muted },
      axisLine: rule,
    },
    yAxis: {
      type: 'value',
      axisLabel: { formatter: (value: number) => format(value), color: colours.muted },
      splitLine: rule,
      splitNumber: 4,
    },
    series: data.series.map((series) => ({
      name: series.label,
      type: 'bar',
      data: [...series.values],
      barGap: '8%',
      itemStyle: { color: colours.series[series.role], borderRadius: [4, 4, 0, 0] },
    })),
  };
}
