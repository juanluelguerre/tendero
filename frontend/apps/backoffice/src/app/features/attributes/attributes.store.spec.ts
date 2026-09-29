import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { AttributesStore } from './attributes.store';

/** The attribute definitions' state (ADR 0047): the list, and how many have a label left untranslated. */
describe('the attributes store', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: 'http://api' }, AttributesStore],
    });
  });

  it('counts the definitions with a label missing in some language', async () => {
    const store = TestBed.inject(AttributesStore);
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne((req) => req.url.endsWith('/api/catalog/attributes'))
      .flush({ items: [{ missingCultures: ['en'] }, { missingCultures: [] }, { missingCultures: ['es', 'en'] }] });
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.items().length, store.incomplete(), store.loading()]).toEqual([3, 2, false]);
  });
});
