import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PaletteStore } from './palette.store';

const Api = 'http://api/api';

/** The command palette's state (ADR 0047): what is typed, what it finds, which option is active. */
describe('the palette store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        PaletteStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.match(() => true);
    vi.useRealTimers();
  });

  const records = () =>
    http.match(
      (req) =>
        req.url.startsWith(`${Api}/orders`) ||
        req.url.startsWith(`${Api}/catalog/products`) ||
        req.url.includes('/agents'),
    );
  /**
   * The search waits 200ms after the last key. It waited 250ms of real time
   * here, and under a loaded machine that 50ms margin was not enough: the
   * debounce starts only when the query's effect has run, so the requests were
   * not out yet and the test failed one run in many. The clock is fake now, and
   * moves the debounce's exact length after the effect has run.
   */
  const debounce = async () => {
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(200);
  };

  /** Steward is reached from the palette too (spec, *The panel*): its option opens the panel, not a screen. */
  it('offers to ask Steward, first, when nothing is typed', () => {
    const store = TestBed.inject(PaletteStore);
    store.opened();

    expect(store.options()[0].kind).toBe('steward');
  });

  it('asks nothing while it is closed, whatever is typed', async () => {
    const store = TestBed.inject(PaletteStore);
    store.typed('ana');
    await debounce();
    TestBed.tick();

    expect(records()).toHaveLength(0);
  });

  it('once open, looks for orders and products by what is typed, and reads the agents once', async () => {
    const store = TestBed.inject(PaletteStore);
    store.opened();
    store.typed('ana');
    await debounce();
    TestBed.tick();

    expect(
      records()
        .map((req) => req.request.url.replace(Api, ''))
        .sort(),
    ).toEqual(['/agents', '/catalog/products', '/orders']);
  });

  /** A new search starts at the top; the arrows wrap around the options there are. */
  it('moves the active option within the options, and starts again when the search changes', () => {
    const store = TestBed.inject(PaletteStore);
    store.opened();
    const count = store.options().length;
    store.step(-1);
    const last = store.active();
    store.typed('x');

    expect([count > 1, last, store.active()]).toEqual([true, count - 1, 0]);
  });
});
