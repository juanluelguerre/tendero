import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { ReturnsPolicy } from './returns-policy.service';

/**
 * The window is a number the shop says out loud and enforces somewhere else.
 *
 * It was already said twice before this existed — `ReturnRequest.Window` in the
 * domain and a literal `{ days: 14 }` in the order page's template — and nothing
 * compared them. Reading it from the server is what stops a third copy arriving
 * with the product page.
 */
describe('the returns policy', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  it('reports the window the API published', () => {
    const policy = TestBed.inject(ReturnsPolicy);

    http.expectOne('http://api/api/returns/policy').flush({ windowDays: 14 });

    expect(policy.windowDays()).toBe(14);
  });

  /**
   * **Not a default of fourteen.** Inventing the number in the browser is the
   * duplication this whole thing exists to remove, and a promise made up by the
   * page is worse than no promise: the shop would keep offering a window it had
   * already shortened. Unknown stays unknown, and the block does not render.
   */
  it('stays unknown when the call fails', () => {
    const policy = TestBed.inject(ReturnsPolicy);

    http.expectOne('http://api/api/returns/policy')
      .flush('down', { status: 503, statusText: 'Service Unavailable' });

    expect(policy.windowDays()).toBeNull();
  });
});
