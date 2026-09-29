import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { beforeEach, describe, expect, it } from 'vitest';
import { BarChart } from './bar-chart';
import type { BarChartData } from './chart-contract';

const DATA: BarChartData = {
  categories: ['Mon', 'Tue'],
  series: [
    { key: 'current', label: 'This week', role: 'data', values: [520, 930] },
    { key: 'previous', label: 'Week before', role: 'previous', values: [470, 720] },
  ],
};

const WORDS = { charts: { figures: { show: 'Show the figures', hide: 'Hide the figures' } } };

beforeEach(() => {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n()] });
  TestBed.inject(CultureStore).use('en');
});

async function render() {
  const fixture = TestBed.createComponent(BarChart);
  fixture.componentRef.setInput('data', DATA);
  fixture.componentRef.setInput('format', (value: number) => `€${value}`);
  fixture.componentRef.setInput('caption', 'Sales by day');
  fixture.detectChanges();
  for (const request of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/'))) request.flush(WORDS);
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('the chart contract', () => {
  it('the table carries every figure whatever draws it', async () => {
    const element = (await render()).nativeElement as HTMLElement;
    const cells = [...element.querySelectorAll('table td')].map((cell) => cell.textContent?.trim());

    expect(cells).toEqual(['€520', '€470', '€930', '€720']);
  });

  it('names the chart by its caption and gives each series a legend', async () => {
    const element = (await render()).nativeElement as HTMLElement;

    expect(element.querySelector('figure figcaption')?.textContent).toContain('Sales by day');
    expect([...element.querySelectorAll('.legend li')].map((item) => item.textContent?.trim())).toEqual(['This week', 'Week before']);
  });

  /** The spec's accessibility section: the figures are there for anyone who asks, not only for a screen reader. */
  it('shows its table of figures to anyone who asks, and hides it again', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;
    const toggle = element.querySelector<HTMLButtonElement>('button.figures-toggle');
    const table = () => element.querySelector('table');

    expect([toggle?.textContent?.trim(), toggle?.getAttribute('aria-expanded')]).toEqual(['Show the figures', 'false']);
    expect(table()?.classList.contains('sr-only')).toBe(true);
    expect(toggle?.getAttribute('aria-controls')).toBe(table()?.id);

    toggle?.click();
    fixture.detectChanges();

    expect([toggle?.textContent?.trim(), toggle?.getAttribute('aria-expanded')]).toEqual(['Hide the figures', 'true']);
    expect(table()?.classList.contains('sr-only')).toBe(false);
  });

  it('draws with ECharts, deferred until the chart is on screen (ADR 0045)', async () => {
    const fixture = await render();

    expect(await fixture.getDeferBlocks()).toHaveLength(1);
  });
});
