import { describe, expect, it } from 'vitest';
import type { ChartColours } from './chart-colours';
import { sparklineOptions } from './echarts-sparkline-options';

const COLOURS: ChartColours = {
  series: { data: 'rgb(42, 105, 176)', previous: 'rgb(107, 101, 95)', agent: 'rgb(184, 69, 31)' },
  text: 'rgb(32, 29, 27)',
  muted: 'rgb(107, 101, 95)',
  border: 'rgb(228, 224, 217)',
  surface: 'rgb(255, 255, 255)',
  font: 'Instrument Sans',
};

type Spark = {
  xAxis: { show: boolean; boundaryGap: boolean };
  yAxis: { show: boolean; scale: boolean };
  series: { type: string; data: number[]; symbol: string; lineStyle: { color: string; width: number } }[];
  animation: boolean;
  tooltip?: unknown;
};

describe('a sparkline', () => {
  it('draws one 2px line in the data colour, with no axes, no markers and no tooltip', () => {
    const options = sparklineOptions([3, 5, 4], COLOURS, false) as unknown as Spark;

    expect(options.series).toEqual([
      expect.objectContaining({
        type: 'line',
        data: [3, 5, 4],
        symbol: 'none',
        lineStyle: { color: COLOURS.series.data, width: 2 },
      }),
    ]);
    expect([options.xAxis.show, options.yAxis.show, options.tooltip]).toEqual([false, false, undefined]);
  });

  /** A trend is its shape: starting the scale at zero would flatten a week that moved between 90 and 110. */
  it('scales to the values rather than from zero, and runs edge to edge', () => {
    const options = sparklineOptions([90, 110], COLOURS, false) as unknown as Spark;

    expect([options.yAxis.scale, options.xAxis.boundaryGap]).toEqual([true, false]);
  });

  it('stays still when the reader asked for less motion', () => {
    expect((sparklineOptions([1], COLOURS, true) as unknown as Spark).animation).toBe(false);
  });
});
