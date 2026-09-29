import { TestBed } from '@angular/core/testing';
import { ThemeStore } from '@tendero/shared-ui';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { BarChartData } from './chart-contract';
import { EchartsBarChart } from './echarts-bar-chart';

const DATA: BarChartData = {
  categories: ['Mon'],
  series: [{ key: 'current', label: 'This week', role: 'data', values: [520] }],
};

describe('the ECharts bar chart', () => {
  // jsdom has no matchMedia; the chart asks it whether the reader wants motion.
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: () => undefined, removeEventListener: () => undefined }));
  afterEach(() => TestBed.inject(ThemeStore).choose('system'));

  /** The bars and the axes are drawn from JavaScript: they follow a theme switch only if the options are recomputed on it. */
  it('reads the tokens again when the theme changes', () => {
    // The directive itself (ECharts in a DOM with no layout) is not what is tested here.
    TestBed.overrideComponent(EchartsBarChart, { set: { imports: [], template: '' } });
    const fixture = TestBed.createComponent(EchartsBarChart);
    const host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('data', DATA);
    fixture.componentRef.setInput('format', String);
    const theme = TestBed.inject(ThemeStore);
    theme.choose('light');
    host.style.setProperty('--chart-data', 'rgb(42, 105, 176)');
    fixture.detectChanges();
    const colourOf = () =>
      ((fixture.componentInstance as unknown as { options: () => { series: { itemStyle: { color: string } }[] } }).options().series[0].itemStyle.color);

    expect(colourOf()).toBe('rgb(42, 105, 176)');

    host.style.setProperty('--chart-data', 'rgb(94, 151, 207)');
    theme.choose('dark');
    fixture.detectChanges();

    expect(colourOf()).toBe('rgb(94, 151, 207)');
  });
});

