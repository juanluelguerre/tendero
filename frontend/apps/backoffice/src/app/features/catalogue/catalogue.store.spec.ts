import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { ProductSummary } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { UndoStore } from '../../worklist/undo/undo.store';
import { CatalogueStore } from './catalogue.store';

const Products = 'http://api/api/catalog/products';

const product = (code: string, status: string) =>
  ({ productId: `id-${code}`, code, name: code, status }) as unknown as ProductSummary;

/** The catalogue's shared state (ADR 0047): status, search, page and open product in the URL, and the changes. */
describe('the catalogue store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        CatalogueStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function at(url: string, total = 45) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(CatalogueStore);
    TestBed.tick();
    http
      .match((req) => req.url === Products && req.method === 'GET')
      .forEach((req) => req.flush({ items: [product('A', 'Active')], total, counts: [] }));
    await new Promise((resolve) => setTimeout(resolve));
    return store;
  }

  /** A page past the last is the last: forty-five products make three pages. */
  it('never goes past the last page', async () => {
    const store = await at('/');
    store.go(9);
    await new Promise((resolve) => setTimeout(resolve));

    expect(TestBed.inject(Router).url).toBe('/?page=3');
  });

  /** Withdrawing offers Undo, which is publishing it again — on the server. */
  it('withdraws a product, shows its new status, and offers to publish it again', async () => {
    const store = await at('/?product=A');
    store.withdraw(product('A', 'Active'));
    http.expectOne(`${Products}/id-A/withdraw`).flush({ status: 'Draft' });
    const status = store.products()[0].status;
    TestBed.inject(UndoStore).take();

    expect([status, http.match(`${Products}/id-A/publish`).length]).toEqual(['Draft', 1]);
  });

  it('says what the import did, and reads the list again', async () => {
    const store = await at('/');
    store.importSeed();
    http.expectOne('http://api/api/catalog/import').flush({ created: 94, updated: 6, failed: 0, elapsedSeconds: 2.4 });
    TestBed.tick();

    expect([
      store.imported()?.created,
      http.match((req) => req.url === Products && req.method === 'GET').length,
    ]).toEqual([94, 1]);
  });
});
