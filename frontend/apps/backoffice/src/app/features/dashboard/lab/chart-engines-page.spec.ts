import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { describe, expect, it } from 'vitest';
import { ChartEnginesPage } from './chart-engines-page';

describe('the chart engines lab (R-9)', () => {
  it('draws the same week with each engine, each in its own box', async () => {
    TestBed.configureTestingModule({
      imports: [ChartEnginesPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n(), { provide: API_BASE_URL, useValue: 'http://api' }],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ChartEnginesPage);
    fixture.detectChanges();
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    fixture.detectChanges();
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('backoffice-svg-engine-box backoffice-svg-bar-chart')).not.toBeNull();
    expect(element.querySelector('backoffice-echarts-engine-box backoffice-bar-chart')).not.toBeNull();
    expect(element.querySelector('backoffice-echarts-engine-box backoffice-svg-bar-chart')).toBeNull();
  });

  it('times one engine at a time and says which', async () => {
    TestBed.configureTestingModule({
      imports: [ChartEnginesPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n(), { provide: API_BASE_URL, useValue: 'http://api' }],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ChartEnginesPage);
    fixture.detectChanges();
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    fixture.detectChanges();
    await fixture.whenStable();

    (fixture.nativeElement.querySelectorAll('button')[0] as HTMLButtonElement).click();
    fixture.detectChanges();

    const statuses = [...(fixture.nativeElement as HTMLElement).querySelectorAll('[role=status]')].map((status) => status.textContent);
    expect(statuses).toHaveLength(1);
    expect(statuses[0]).toMatch(/^SVG: 50 redraws, [\d.]+ ms of work/);
  });
});
