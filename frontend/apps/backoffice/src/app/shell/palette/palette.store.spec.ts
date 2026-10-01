import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { PaletteStore } from './palette.store';

const Api = 'http://api/api';

/** The command palette's state (ADR 0047): what is typed, what it finds, which option is active. */
describe('the palette store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
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

  afterEach(() => http.match(() => true));

  const records = () =>
    http.match(
      (req) =>
        req.url.startsWith(`${Api}/orders`) ||
        req.url.startsWith(`${Api}/catalog/products`) ||
        req.url.includes('/agents'),
    );
  const debounce = () => new Promise((resolve) => setTimeout(resolve, 250));

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
