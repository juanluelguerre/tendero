import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import type { BarChartData } from './chart-contract';
import { SvgBarChart } from './svg-bar-chart';

const WEEK: BarChartData = {
  categories: ['Mon', 'Tue', 'Wed'],
  series: [
    { key: 'current', label: 'This week', role: 'data', values: [520, 930, 0] },
    { key: 'previous', label: 'Week before', role: 'previous', values: [470, 720, 560] },
  ],
};

function draw(data: BarChartData) {
  const fixture = TestBed.createComponent(SvgBarChart);
  fixture.componentRef.setInput('data', data);
  fixture.componentRef.setInput('format', (value: number) => `€${value}`);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('our own SVG bar chart', () => {
  it('draws one group per category and one bar per series, proportional to the round maximum', () => {
    const bars = [...draw(WEEK).querySelectorAll('rect[data-series]')];

    expect(bars).toHaveLength(6);
    // 930 of a round 1000: 93 % of the plot's height.
    const tallest = bars.find((bar) => bar.getAttribute('data-value') === '930');
    expect(tallest?.getAttribute('height')).toBe('93');
  });

  it('colours a bar by its role, through a token', () => {
    const current = draw(WEEK).querySelector('rect[data-series="current"]') as SVGRectElement;

    expect(current.getAttribute('style')).toContain('var(--chart-data)');
  });

  it('names each bar for a pointer and a keyboard', () => {
    const titles = [...draw(WEEK).querySelectorAll('rect[data-series] title')].map((title) => title.textContent);

    expect(titles).toContain('Tue · This week · €930');
  });
});
