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
  it.each(['columns', 'rows', 'share'] as const)('carries no colour but the ones it is given, as %s', (layout) => {
    const written = JSON.stringify(barOptions(DATA, String, COLOURS, false, layout));
    const given = new Set([...Object.values(COLOURS.series), COLOURS.text, COLOURS.muted, COLOURS.border, COLOURS.surface]);

    expect(written.match(/#[0-9a-f]{3,8}\b/gi)).toBeNull();
    expect((written.match(/rgba?\([^)]*\)/g) ?? []).filter((colour) => !given.has(colour))).toEqual([]);
  });
});

describe('bars in rows and as a share', () => {
  const REASONS: BarChartData = {
    categories: ['Damaged', 'Wrong size', 'Changed mind'],
    series: [{ key: 'open', label: 'Open returns', role: 'data', values: [5, 3, 1] }],
  };
  const CHANNELS: BarChartData = {
    categories: ['Orders'],
    series: [
      { key: 'people', label: 'People', role: 'data', values: [30] },
      { key: 'agents', label: 'Agents', role: 'agent', values: [10] },
    ],
  };
  type Axis = { type: string; data?: string[]; inverse?: boolean; show?: boolean; max?: number };
  type Layout = { xAxis: Axis; yAxis: Axis; series: { stack?: string; itemStyle: { borderColor?: string; borderWidth?: number } }[] };

  it('puts the categories on the left, first at the top, when the bars are rows', () => {
    const options = barOptions(REASONS, String, COLOURS, false, 'rows') as unknown as Layout;

    expect([options.yAxis.type, options.yAxis.data, options.yAxis.inverse, options.xAxis.type]).toEqual([
      'category', ['Damaged', 'Wrong size', 'Changed mind'], true, 'value',
    ]);
  });

  it('stacks the series into one bar whose length is the whole, with no axes to read', () => {
    const options = barOptions(CHANNELS, String, COLOURS, false, 'share') as unknown as Layout;

    expect(options.series.map((series) => series.stack)).toEqual(['share', 'share']);
    expect([options.xAxis.max, options.xAxis.show, options.yAxis.show]).toEqual([40, false, false]);
  });

  /** The dataviz method's spacer: adjacent fills are parted by 2px of the surface, so two segments never read as one. */
  it('parts the segments of a share with the surface', () => {
    const options = barOptions(CHANNELS, String, COLOURS, false, 'share') as unknown as Layout;

    expect(options.series.map((series) => [series.itemStyle.borderColor, series.itemStyle.borderWidth])).toEqual([
      [COLOURS.surface, 2], [COLOURS.surface, 2],
    ]);
  });

  it('keeps drawing columns when no layout is named', () => {
    const options = barOptions(DATA, String, COLOURS, false) as unknown as Layout;

    expect([options.xAxis.type, options.yAxis.type]).toEqual(['category', 'value']);
  });
});
