import { describe, expect, it } from 'vitest';
import type { ChartColours } from './chart-colours';
import type { BarChartData } from './chart-contract';
import { barOptions } from './echarts-bar-options';

const DATA: BarChartData = {
  categories: ['Mon', 'Tue'],
  series: [
    { key: 'current', label: 'This week', role: 'data', values: [520, 930] },
    { key: 'previous', label: 'Week before', role: 'previous', values: [470, 720] },
  ],
};

const COLOURS: ChartColours = {
  series: { data: 'rgb(42, 105, 176)', previous: 'rgb(107, 101, 95)', agent: 'rgb(184, 69, 31)' },
  text: 'rgb(32, 29, 27)',
  muted: 'rgb(107, 101, 95)',
  border: 'rgb(228, 224, 217)',
  surface: 'rgb(255, 255, 255)',
  font: 'Instrument Sans',
};

type Options = {
  series: { name: string; data: number[]; itemStyle: { color: string } }[];
  xAxis: { data: string[]; axisLabel: { color: string }; axisLine: { lineStyle: { color: string } } };
  yAxis: { axisLabel: { color: string }; splitLine: { lineStyle: { color: string } } };
  tooltip: { backgroundColor: string; borderColor: string; textStyle: { color: string } };
  textStyle: { color: string; fontFamily: string };
  aria: { enabled: boolean };
  animation: boolean;
};

describe('ECharts options for a bar chart', () => {
  it('draws one bar series per series, in its role colour from the tokens', () => {
    const options = barOptions(DATA, String, COLOURS, false) as unknown as Options;

    expect(options.series.map((one) => [one.name, one.data, one.itemStyle.color])).toEqual([
      ['This week', [520, 930], 'rgb(42, 105, 176)'],
      ['Week before', [470, 720], 'rgb(107, 101, 95)'],
    ]);
  });

  it('keeps the categories, describes itself to a screen reader, and stays still when asked', () => {
    const options = barOptions(DATA, String, COLOURS, true) as unknown as Options;

    expect(options.xAxis.data).toEqual(['Mon', 'Tue']);
    expect(options.aria.enabled).toBe(true);
    expect(options.animation).toBe(false);
  });

  /** ECharts' own light theme would draw the axes, the grid and the tooltip; in the dark theme its grey text reads at 3.3:1. */
  it('draws its text, axes, grid and tooltip in the tokens colours too', () => {
    const options = barOptions(DATA, String, COLOURS, false) as unknown as Options;

    expect(options.textStyle).toEqual({ color: COLOURS.text, fontFamily: COLOURS.font });
    expect([options.xAxis.axisLabel.color, options.yAxis.axisLabel.color]).toEqual([COLOURS.muted, COLOURS.muted]);
    expect([options.xAxis.axisLine.lineStyle.color, options.yAxis.splitLine.lineStyle.color]).toEqual([COLOURS.border, COLOURS.border]);
    expect(options.tooltip).toMatchObject({ backgroundColor: COLOURS.surface, borderColor: COLOURS.border, textStyle: { color: COLOURS.text } });
  });

  /** ADR 0045: a colour reaches ECharts only from the tokens, never written into the options. */
  it('carries no colour but the ones it is given', () => {
    const written = JSON.stringify(barOptions(DATA, String, COLOURS, false));
    const given = new Set([...Object.values(COLOURS.series), COLOURS.text, COLOURS.muted, COLOURS.border, COLOURS.surface]);

    expect(written.match(/#[0-9a-f]{3,8}\b/gi)).toBeNull();
    expect((written.match(/rgba?\([^)]*\)/g) ?? []).filter((colour) => !given.has(colour))).toEqual([]);
  });
});
