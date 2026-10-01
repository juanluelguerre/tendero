import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AuditStore } from './audit.store';

const Audit = 'http://api/api/audit';

/** The audit log's state (ADR 0047): the outcome filter, the page, the open payload, and the log. */
describe('the audit store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        AuditStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  const asked = () => http.match((req) => req.url === Audit).map((req) => req.request.params.toString());

  async function answer(
    counts = [
      { outcome: 'Denied', count: 3 },
      { outcome: 'Allowed', count: 7 },
    ],
  ) {
    http.match((req) => req.url === Audit).forEach((req) => req.flush({ entries: [{ id: 'e1' }], total: 3, counts }));
    await TestBed.inject(ApplicationRef).whenStable();
  }

  /** What the shopkeeper opens the log for: what was refused. */
  it('opens on what was denied, first page', () => {
    TestBed.inject(AuditStore);
    TestBed.tick();

    expect(asked()).toEqual(['page=1&pageSize=20&outcome=Denied']);
  });

  it('starts at the first page and closes the payload when the filter changes', async () => {
    const store = TestBed.inject(AuditStore);
    TestBed.tick();
    await answer();
    store.go(2);
    store.toggle('e1');
    store.use(null);
    TestBed.tick();

    expect([store.page(), store.opened(), asked().slice(-1)[0]]).toEqual([1, null, 'page=1&pageSize=20']);
  });

  /** Every chip says its number whichever is chosen, and the whole log's, not the page's. */
  it('counts every outcome and all of them', async () => {
    const store = TestBed.inject(AuditStore);
    TestBed.tick();
    await answer();

    expect(store.counts()).toEqual({
      byOutcome: new Map([
        ['Denied', 3],
        ['Allowed', 7],
      ]),
      all: 10,
    });
  });

  it('keeps one payload open at a time', () => {
    const store = TestBed.inject(AuditStore);
    store.toggle('e1');
    store.toggle('e2');
    const second = store.opened();
    store.toggle('e2');

    expect([second, store.opened()]).toEqual(['e2', null]);
  });
});
