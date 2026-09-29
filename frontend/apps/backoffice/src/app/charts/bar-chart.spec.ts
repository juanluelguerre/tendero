import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BarChart } from './bar-chart';
import type { BarChartData } from './chart-contract';

const DATA: BarChartData = {
  categories: ['Mon', 'Tue'],
  series: [
    { key: 'current', label: 'This week', role: 'data', values: [520, 930] },
    { key: 'previous', label: 'Week before', role: 'previous', values: [470, 720] },
  ],
};

function render() {
  const fixture = TestBed.createComponent(BarChart);
  fixture.componentRef.setInput('data', DATA);
  fixture.componentRef.setInput('format', (value: number) => `€${value}`);
  fixture.componentRef.setInput('caption', 'Sales by day');
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('the chart contract', () => {
  it('the table carries every figure whatever draws it', () => {
    const cells = [...render().querySelectorAll('table td')].map((cell) => cell.textContent?.trim());

    expect(cells).toEqual(['€520', '€470', '€930', '€720']);
  });

  it('names the chart by its caption and gives each series a legend', () => {
    const element = render();

    expect(element.querySelector('figure figcaption')?.textContent).toContain('Sales by day');
    expect([...element.querySelectorAll('.legend li')].map((item) => item.textContent?.trim())).toEqual(['This week', 'Week before']);
  });

  it('draws with ECharts, deferred until the chart is on screen (ADR 0045)', async () => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(BarChart);
    fixture.componentRef.setInput('data', DATA);
    fixture.componentRef.setInput('format', (value: number) => `€${value}`);
    fixture.componentRef.setInput('caption', 'Sales by day');
    fixture.detectChanges();

    expect(await fixture.getDeferBlocks()).toHaveLength(1);
  });
});
