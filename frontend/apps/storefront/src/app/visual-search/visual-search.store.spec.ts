import { ApplicationRef, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, type TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VisualSearchStore } from './visual-search.store';

const Status = 'http://api/api/search/by-image/status';
const Ask = (request: { url: string }) => request.url === 'http://api/api/search/by-image';

const lamp = { product: { productId: 'p-1', code: 'LAMP' }, picture: 'img-lamp-brass', similarity: 0.9 };

/** Searching the shop with a picture: whether it can be, the picture given, and the three ways it is not answered. */
describe('searching with a picture', () => {
  let http: HttpTestingController;
  const telemetry = { trackEvent: vi.fn() };
  let store: InstanceType<typeof VisualSearchStore>;
  const picture = new Blob(['a picture'], { type: 'image/png' });

  beforeEach(() => {
    telemetry.trackEvent.mockClear();
    // jsdom has neither: the address is the browser's own, and so is taking it back.
    URL.createObjectURL = vi.fn(() => 'blob:mine');
    URL.revokeObjectURL = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        { provide: API_BASE_URL, useValue: 'http://api' },
        { provide: Telemetry, useValue: telemetry },
        { provide: CultureStore, useValue: { active: signal('es') } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(VisualSearchStore);
  });

  afterEach(() => {
    http.match(Status);
    http.verify();
  });

  async function says(...answer: Parameters<ReturnType<HttpTestingController['expectOne']>['flush']>) {
    TestBed.tick();
    http.expectOne(Status).flush(...answer);
    await TestBed.inject(ApplicationRef).whenStable();
  }

  /** The request for a picture, once the browser has had its turn at making it smaller. */
  async function asked() {
    let found: TestRequest[] = [];
    await vi.waitFor(() => {
      found = http.match(Ask);
      expect(found.length).toBe(1);
    });
    return found[0];
  }

  /** Only a yes draws the camera: the box must be the one it always was everywhere else (invariant 8). */
  it('is available only once the server has said yes', async () => {
    expect(store.available()).toBe(false);

    await says({ available: true });

    expect(store.available()).toBe(true);
  });

  it('is not available when the server says no, or does not answer', async () => {
    await says({ available: false });
    expect(store.available()).toBe(false);
  });

  it('is not available when the status cannot be read', async () => {
    await says('down', { status: 503, statusText: 'Service Unavailable' });

    expect(store.available()).toBe(false);
  });

  it('sends the picture as the form’s one file, in the page’s language, and shows the answer on its own page', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    store.open();

    store.give({ picture, how: 'dropped' });
    expect([store.phase(), store.preview()]).toEqual(['looking', 'blob:mine']);

    const request = await asked();
    expect(request.request.method).toBe('POST');
    expect(request.request.params.get('culture')).toBe('es');
    expect((request.request.body as FormData).get('picture')).toBeInstanceOf(Blob);
    request.flush({ items: [lamp] });

    expect([store.phase(), store.isOpen(), store.matches()]).toEqual(['idle', false, [lamp]]);
    expect(navigate).toHaveBeenCalledWith(['/', 'es', 'search', 'picture']);
    expect(telemetry.trackEvent).toHaveBeenCalledWith('search_by_image_submitted', {
      how: 'dropped',
      outcome: 'answered',
      results: 1,
    });
  });

  it.each([
    [400, 'refused', 'refused'],
    [413, 'refused', 'refused'],
    [429, 'tooMany', 'too_many'],
    [503, 'down', 'down'],
    [500, 'down', 'down'],
  ] as const)(
    'a %i says %s beside the picture, keeps the panel open and goes nowhere',
    async (status, phase, outcome) => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      store.open();

      store.give({ picture, how: 'chosen' });
      (await asked()).flush('no', { status, statusText: 'No' });

      expect([store.phase(), store.isOpen(), store.matches()]).toEqual([phase, true, null]);
      expect(navigate).not.toHaveBeenCalled();
      expect(telemetry.trackEvent).toHaveBeenCalledWith('search_by_image_submitted', {
        how: 'chosen',
        outcome,
        results: 0,
      });
    },
  );

  /** One picture refused must not be the last picture that can be given. */
  it('takes another picture after one was refused, and lets go of the first one’s address', async () => {
    store.give({ picture, how: 'chosen' });
    (await asked()).flush({ code: 'Refused', status: 400, title: 'no' }, { status: 400, statusText: 'Bad Request' });

    store.give({ picture, how: 'pasted' });
    (await asked()).flush({ items: [] });

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mine');
    expect([store.phase(), store.matches()]).toEqual(['idle', []]);
  });

  it('opening the panel again clears what the last picture was told', async () => {
    store.give({ picture, how: 'chosen' });
    (await asked()).flush({ code: 'Refused', status: 429, title: 'no' }, { status: 429, statusText: 'Too Many' });
    store.close();

    store.toggle();

    expect([store.isOpen(), store.phase()]).toEqual([true, 'idle']);
  });

  it('counts a product followed from the answer by its place in it', () => {
    store.followed(3);

    expect(telemetry.trackEvent).toHaveBeenCalledWith('search_by_image_result_followed', { position: 3 });
  });
});
