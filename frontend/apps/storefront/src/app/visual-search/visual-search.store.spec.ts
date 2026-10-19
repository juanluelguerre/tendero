import { ApplicationRef, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { VisualMatch } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VisualSearchStore } from './visual-search.store';

const Status = 'http://api/api/search/by-image/status';

// Only what the store passes on: the rest of a card is the card's.
const lamp = {
  product: { productId: 'p-1', code: 'LAMP' },
  picture: 'img-lamp-brass',
  similarity: 0.9,
} as unknown as VisualMatch;

/** Searching the shop with a picture: whether it can be, and the page of one list. */
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

  it('can find a picture’s objects only when the server says so', async () => {
    await says({ available: true, objects: false });
    expect([store.available(), store.objects()]).toEqual([true, false]);
  });

  it('puts one block’s whole list on the page of its own, with the crop it answers', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    store.open();

    store.show([lamp], picture, 2);

    expect([store.isOpen(), store.matches(), store.preview(), store.block()]).toEqual([false, [lamp], 'blob:mine', 2]);
    expect(navigate).toHaveBeenCalledWith(['/', 'es', 'search', 'picture']);

    // A second list lets go of the first one's address.
    store.show([], picture, 1);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mine');
  });

  it('counts a product followed from the answer by its place in it and the object it answered', () => {
    store.show([lamp], picture, 2);
    store.followed(3);

    expect(telemetry.trackEvent).toHaveBeenCalledWith('search_by_image_result_followed', { position: 3, block: 2 });
  });
});
