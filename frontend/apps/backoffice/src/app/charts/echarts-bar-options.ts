import type { EChartsCoreOption } from 'echarts/core';
import type { ChartColours } from './chart-colours';
import type { BarChartData, BarLayout, ValueFormat } from './chart-contract';

/** A bar chart's options, as a pure function of the contract's data: tested without a canvas. */
export function barOptions(
  data: BarChartData,
  format: ValueFormat,
  colours: ChartColours,
  reducedMotion: boolean,
  layout: BarLayout = 'columns',
): EChartsCoreOption {
  const rule = { lineStyle: { color: colours.border } };
  const categoryAxis = {
    type: 'category',
    data: [...data.categories],
    axisTick: { show: false },
    axisLabel: { color: colours.muted },
    axisLine: rule,
  };
  const valueAxis = {
    type: 'value',
    axisLabel: { formatter: (value: number) => format(value), color: colours.muted },
    splitLine: rule,
    splitNumber: 4,
  };
  const whole = data.series.reduce((sum, series) => sum + (series.values[0] ?? 0), 0);

  const axes =
    layout === 'rows'
      ? { xAxis: valueAxis, yAxis: { ...categoryAxis, inverse: true } }
      : layout === 'share'
        ? {
            xAxis: { type: 'value', max: whole, show: false },
            yAxis: { type: 'category', data: [...data.categories], show: false },
          }
        : { xAxis: categoryAxis, yAxis: valueAxis };

  const radius = layout === 'rows' ? [0, 4, 4, 0] : layout === 'share' ? 0 : [4, 4, 0, 0];

  return {
    animation: !reducedMotion,
    aria: { enabled: true },
    textStyle: { color: colours.text, fontFamily: colours.font },
    grid:
      layout === 'share'
        ? { left: 0, right: 0, top: 0, bottom: 0 }
        : { left: 8, right: 8, top: 8, bottom: 24, containLabel: true },
    tooltip: {
      trigger: layout === 'share' ? 'item' : 'axis',
      valueFormatter: (value: unknown) => format(Number(value)),
      backgroundColor: colours.surface,
      borderColor: colours.border,
      textStyle: { color: colours.text },
    },
    ...axes,
    series: data.series.map((series) => ({
      name: series.label,
      type: 'bar',
      data: [...series.values],
      barGap: '8%',
      ...(layout === 'share' ? { stack: 'share', barWidth: '100%' } : {}),
      itemStyle: {
        color: colours.series[series.role],
        borderRadius: radius,
        // The dataviz method's spacer: two segments of one whole are parted by the surface.
        ...(layout === 'share' ? { borderColor: colours.surface, borderWidth: 2 } : {}),
      },
    })),
  };
}
