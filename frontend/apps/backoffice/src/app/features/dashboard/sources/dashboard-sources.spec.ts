import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { DashboardSources } from './dashboard-sources';

const Sales = 'http://api/api/dashboard/sales';

describe('the dashboard sources', () => {
  let http: HttpTestingController;
  let sources: DashboardSources;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        DashboardSources,
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    sources = TestBed.inject(DashboardSources);
  });

  /** Review focus 4: key figures and best sellers both read /sales — one request, not two. */
  it('asks an endpoint once for every widget that reads it in the same period and instant', async () => {
    const first = firstValueFrom(sources.sales('Week', 1));
    const second = firstValueFrom(sources.sales('Week', 1));
    http.expectOne((req) => req.url === Sales).flush({ period: 'Week' });

    expect([await first, await second]).toEqual([{ period: 'Week' }, { period: 'Week' }]);
  });

  it('asks again for another period, and for a later instant', () => {
    sources.sales('Week', 1).subscribe();
    sources.sales('Month', 1).subscribe();
    sources.sales('Week', 2).subscribe();

    expect(http.match((req) => req.url === Sales)).toHaveLength(3);
  });

  /** Review focus 2: a failure is not an answer to keep — the next widget or the next refresh asks again. */
  it('forgets a read that failed', async () => {
    const failed = firstValueFrom(sources.sales('Week', 1)).catch(() => 'failed');
    http.expectOne((req) => req.url === Sales).flush('down', { status: 503, statusText: 'Unavailable' });
    expect(await failed).toBe('failed');

    sources.sales('Week', 1).subscribe();
    http.expectOne((req) => req.url === Sales);
  });
});
