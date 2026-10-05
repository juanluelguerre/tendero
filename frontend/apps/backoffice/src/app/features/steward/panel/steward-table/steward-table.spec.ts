import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { ResultTable } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { beforeEach, describe, expect, it } from 'vitest';
import { StewardTable } from './steward-table';

const words = {
  steward: {
    tables: { salesFigures: 'Sales' },
    columns: { period: 'Period', revenue: 'Revenue', orders: 'Orders', sku: 'SKU' },
    terms: { current: 'This period', Damaged: 'Damaged' },
  },
};

const sales: ResultTable = {
  key: 'salesFigures',
  currency: 'EUR',
  columns: [
    { key: 'period', kind: 'Term' },
    { key: 'revenue', kind: 'Money' },
    { key: 'orders', kind: 'Number' },
  ],
  rows: [
    [
      { text: 'current', number: null },
      { text: null, number: 1234.5 },
      { text: null, number: 12 },
    ],
  ],
};

/** The rows a read returned, drawn as the server built them: figures in the reader's culture, the shop's words translated. */
describe('the steward table', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n()],
    }),
  );

  const drawn = (table: ResultTable, culture: 'es' | 'en' = 'en') => {
    TestBed.inject(CultureStore).use(culture);
    const fixture = TestBed.createComponent(StewardTable);
    fixture.componentRef.setInput('table', table);
    fixture.detectChanges();
    for (const request of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/')))
      request.flush(words);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('titles the table and its columns in the reader’s words', () => {
    const element = drawn(sales);

    expect(element.querySelector('figcaption')?.textContent).toBe('Sales');
    expect([...element.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'Period',
      'Revenue',
      'Orders',
    ]);
  });

  it('writes money and numbers in the reader’s culture, and translates a term', () => {
    const cells = [...drawn(sales).querySelectorAll('td')].map((cell) => cell.textContent?.trim());

    expect(cells).toEqual(['This period', '€1,234.50', '12']);
  });

  it('lines figures up to the end and leaves words at the start', () => {
    const cells = [...drawn(sales).querySelectorAll('td')];

    expect(cells.map((cell) => cell.classList.contains('text-end'))).toEqual([false, true, true]);
  });

  it('shows a dash where the read had nothing', () => {
    const empty: ResultTable = {
      key: 'salesFigures',
      currency: null,
      columns: [{ key: 'sku', kind: 'Text' }],
      rows: [[{ text: null, number: null }]],
    };

    expect(drawn(empty).querySelector('td')?.textContent?.trim()).toBe('—');
  });

  it('never draws a cell as markup', () => {
    const hostile: ResultTable = {
      key: 'salesFigures',
      currency: null,
      columns: [{ key: 'sku', kind: 'Text' }],
      rows: [[{ text: '<img src=x onerror=alert(1)>', number: null }]],
    };
    const element = drawn(hostile);

    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('td')?.textContent).toContain('<img');
  });
});
