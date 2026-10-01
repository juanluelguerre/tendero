import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { PromotionsStore } from './promotions.store';

/** The promotions' state (ADR 0047): the list, and how many are running. */
describe('the promotions store', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        PromotionsStore,
      ],
    });
  });

  it('counts the promotions that are running', async () => {
    const store = TestBed.inject(PromotionsStore);
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne((req) => req.url.endsWith('/api/pricing/promotions'))
      .flush({ items: [{ isActive: true }, { isActive: false }, { isActive: true }] });
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.items().length, store.running(), store.failed()]).toEqual([3, 2, false]);
  });
});
