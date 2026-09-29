import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { DefineVariantsStore } from './define-variants.store';

const Variants = 'http://api/api/catalog/products/p1/variants';

/** Defining variants (ADR 0047): the axes being drafted, how many variants they make, and what the server answered. */
describe('the define variants store', () => {
  let store: InstanceType<typeof DefineVariantsStore>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: 'http://api' }, DefineVariantsStore],
    });
    store = TestBed.inject(DefineVariantsStore);
    http = TestBed.inject(HttpTestingController);
  });

  function draft() {
    store.edit(0, 'code', ' COLOR ');
    store.edit(0, 'options', 'NAVY, BLACK,');
    store.addAxis();
    store.edit(1, 'code', 'SIZE');
    store.edit(1, 'options', 'S,M,L');
    store.addAxis();
  }

  /** Shown before pressing: the cartesian product grows faster than intuition expects. An axis with no options counts for nothing. */
  it('says how many variants the axes make', () => {
    draft();

    expect(store.combinations()).toBe(6);
  });

  it('sends only the complete axes, trimmed, and says how many were created', () => {
    draft();
    store.generate('p1');
    const request = http.expectOne(Variants);
    const sent = request.request.body.axes;
    request.flush({ created: 6 });

    expect([sent, store.created(), store.busy()]).toEqual([
      [
        { code: 'COLOR', options: ['NAVY', 'BLACK'] },
        { code: 'SIZE', options: ['S', 'M', 'L'] },
      ],
      6,
      false,
    ]);
  });

  /** The server's 409 carries the domain's reason; a session that ended says nothing — the door is on its way. */
  it("shows the server's reason, and nothing when the session ended", () => {
    draft();
    store.generate('p1');
    http.expectOne(Variants).flush('The product already has variants.', { status: 409, statusText: 'Conflict' });
    const refused = store.failed();
    store.generate('p1');
    http.expectOne(Variants).flush('', { status: 401, statusText: 'Unauthorized' });

    expect([refused, store.failed()]).toEqual(['The product already has variants.', null]);
  });
});
