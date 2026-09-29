import { describe, expect, it } from 'vitest';
import type { BarChartData } from './chart-contract';
import { barOptions } from './echarts-bar-options';

const DATA: BarChartData = {
  categories: ['Mon', 'Tue'],
  series: [
    { key: 'current', label: 'This week', role: 'data', values: [520, 930] },
    { key: 'previous', label: 'Week before', role: 'previous', values: [470, 720] },
  ],
};
const COLOURS = { data: 'rgb(42, 105, 176)', previous: 'rgb(107, 101, 95)', agent: 'rgb(184, 69, 31)' } as const;

describe('ECharts options for a bar chart', () => {
  it('draws one bar series per series, in its role colour from the tokens', () => {
    const options = barOptions(DATA, String, COLOURS, false) as { series: { name: string; data: number[]; itemStyle: { color: string } }[] };

    expect(options.series.map((one) => [one.name, one.data, one.itemStyle.color])).toEqual([
      ['This week', [520, 930], 'rgb(42, 105, 176)'],
      ['Week before', [470, 720], 'rgb(107, 101, 95)'],
    ]);
  });

  it('keeps the categories, describes itself to a screen reader, and stays still when asked', () => {
    const options = barOptions(DATA, String, COLOURS, true) as { xAxis: { data: string[] }; aria: { enabled: boolean }; animation: boolean };

    expect(options.xAxis.data).toEqual(['Mon', 'Tue']);
    expect(options.aria.enabled).toBe(true);
    expect(options.animation).toBe(false);
  });

  /** ADR 0045: a colour reaches ECharts only from the tokens, never written into the options. */
  it('carries no colour but the ones it is given', () => {
    const written = JSON.stringify(barOptions(DATA, String, COLOURS, false));

    expect(written.match(/#[0-9a-f]{3,8}\b/gi)).toBeNull();
    expect(written.match(/rgba?\([^)]*\)/g)?.sort()).toEqual([COLOURS.data, COLOURS.previous].sort());
  });
});
