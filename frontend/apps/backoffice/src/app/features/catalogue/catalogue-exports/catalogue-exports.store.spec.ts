import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { CatalogueExportsStore } from './catalogue-exports.store';

/** Taking the catalogue to another platform (phase 16, E-3): one request, a file back, and a sentence when it fails. */
describe('the catalogue exports store', () => {
  let http: HttpTestingController;
  let store: InstanceType<typeof CatalogueExportsStore>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        CatalogueExportsStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(CatalogueExportsStore);
  });

  it('asks for the catalogue in the platform format and hands back the file', async () => {
    const downloading = store.downloadCatalogue('shopify-csv');
    expect(store.downloading()).toBe(true);

    const request = http.expectOne((req) => req.url === 'http://api/api/exports/catalogue');
    expect([request.request.method, request.request.params.get('format')]).toEqual(['POST', 'shopify-csv']);
    request.flush(new Blob(['Title,URL handle\r\n'], { type: 'text/csv' }));

    const file = await downloading;
    expect(file?.size).toBeGreaterThan(0);
    expect([store.downloading(), store.problem()]).toEqual([false, null]);
  });

  it('says so when the catalogue could not be taken, and hands back nothing', async () => {
    const downloading = store.downloadCatalogue('shopify-csv');

    http
      .expectOne((req) => req.url === 'http://api/api/exports/catalogue')
      .flush(new Blob(), { status: 500, statusText: 'Server Error' });

    expect(await downloading).toBeNull();
    expect(store.downloading()).toBe(false);
    expect(store.problem()).not.toBeNull();
  });
});
