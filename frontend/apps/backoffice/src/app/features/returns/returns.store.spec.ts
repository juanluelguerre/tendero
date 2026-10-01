import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { ReturnView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ReturnsStore } from './returns.store';

const Returns = 'http://api/api/returns';

const request = (returnId: string, status: string) => ({ returnId, status }) as unknown as ReturnView;

/** The returns' shared state (ADR 0047): the requests, the filter and the open one in the URL, and the decisions. */
describe('the returns store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        ReturnsStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(ReturnsStore);
    TestBed.tick();
    http
      .match(Returns)
      .forEach((req) => req.flush([request('r1', 'Requested'), request('r2', 'Approved'), request('r3', 'Requested')]));
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  /** The job this screen opens on is deciding what was asked for. */
  it('opens on the requests waiting for a decision, and counts every status', async () => {
    const store = await at('/');

    expect([
      store.statusKey(),
      store.shown().map((row) => row.returnId),
      store.filters().map((chip) => `${chip.key}:${chip.count}`),
    ]).toEqual(['Requested', ['r1', 'r3'], ['Requested:2', 'Approved:1', 'Received:0', 'all:3']]);
  });

  it('replaces the request a decision comes back with, and keeps the server sentence when it refuses', async () => {
    const store = await at('/?status=all');
    store.decide(request('r1', 'Requested'), 'Approve', 'row');
    http.expectOne(`${Returns}/r1/decide`).flush(request('r1', 'Approved'));
    store.decide(request('r2', 'Approved'), 'Refund', 'pane');
    http.expectOne(`${Returns}/r2/decide`).flush('Not received yet.', { status: 409, statusText: 'Conflict' });

    expect([store.shown().map((row) => row.status), store.refusal()]).toEqual([
      ['Approved', 'Approved', 'Requested'],
      { r2: 'Not received yet.' },
    ]);
  });
});
