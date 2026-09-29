import type { EChartsCoreOption } from 'echarts/core';
import type { ChartColours } from './chart-colours';

/** A key figure's trend (R-9): its shape only — the number and its change sit beside it. */
export function sparklineOptions(values: readonly number[], colours: ChartColours, reducedMotion: boolean): EChartsCoreOption {
  return {
    animation: !reducedMotion,
    grid: { left: 1, right: 1, top: 2, bottom: 2 },
    xAxis: { type: 'category', show: false, boundaryGap: false, data: values.map((_, index) => index) },
    yAxis: { type: 'value', show: false, scale: true },
    series: [{ type: 'line', data: [...values], symbol: 'none', lineStyle: { color: colours.series.data, width: 2 } }],
  };
}
