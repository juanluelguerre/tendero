import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import type { AddressBookView, AddressEntryView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { of, Subject, throwError } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { AddressBookService } from '../data-access/address-book.service';
import { CustomerLink } from '../data-access/customer-link';
import { AddressBookStore } from './address-book.store';

const address = {
  recipientName: 'Ana García',
  line1: 'Calle Mayor 12',
  line2: '3º B',
  city: 'Madrid',
  region: null,
  postalCode: '28013',
  countryCode: 'ES',
  phone: null,
};

const entry = (id: string, label: string, delivery = false, billing = false): AddressEntryView => ({
  id,
  label,
  address,
  deliveryDefault: delivery,
  billingDefault: billing,
});

const book = (...entries: AddressEntryView[]): AddressBookView => ({ entries, maxEntries: 10 });

/** The shopper's address book (P15-2): the server's book is the only one, and every change replaces it. */
describe('the address book store', () => {
  let signedIn: boolean;
  let calls: string[];
  let service: Partial<Record<keyof AddressBookService, (...args: unknown[]) => unknown>>;

  beforeEach(() => {
    signedIn = true;
    calls = [];
    service = {
      get: () => (calls.push('get'), of(book(entry('A', 'Home', true, true)))),
      add: () => of(book(entry('A', 'Home', true, true), entry('B', 'Work'))),
      remove: () => of(book(entry('B', 'Work'))),
    };
  });

  function opened() {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthStore, useValue: { isSignedIn: () => signedIn } },
        { provide: CustomerLink, useValue: { ensure: async () => void calls.push('link') } },
        { provide: AddressBookService, useValue: service },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
      ],
    });
    return TestBed.inject(AddressBookStore);
  }

  it('reads nothing for a guest', async () => {
    signedIn = false;
    const store = opened();

    await store.load();

    expect([calls, store.status(), store.entries()]).toEqual([[], 'idle', []]);
  });

  /** A book cannot be read for somebody the shop has not linked yet, so the link comes first. */
  it('links the customer, then reads the book and its defaults', async () => {
    const store = opened();

    await store.load();

    expect(calls).toEqual(['link', 'get']);
    expect([store.status(), store.deliveryDefault()?.label, store.billingDefault()?.label]).toEqual([
      'ready',
      'Home',
      'Home',
    ]);
  });

  /** Removing the default leaves none: the store shows the server's answer, it does not promote one itself. */
  it('replaces the book with what a change answered', async () => {
    const store = opened();
    await store.load();

    expect(await store.remove('A')).toBe(true);

    expect(store.entries().map((kept) => kept.label)).toEqual(['Work']);
    expect(store.deliveryDefault()).toBeNull();
  });

  it('says a refusal in the server’s own sentence and keeps the book as it was', async () => {
    service.add = () =>
      throwError(() => ({
        status: 409,
        error: { code: 'AddressBookFull', title: 'Ya tienes 10 direcciones. Quita una para guardar otra.' },
      }));
    const store = opened();
    await store.load();

    expect(await store.add({ label: 'More', address, deliveryDefault: false, billingDefault: false })).toBe(false);

    expect(store.problem()).toBe('Ya tienes 10 direcciones. Quita una para guardar otra.');
    expect(store.entries().map((kept) => kept.label)).toEqual(['Home']);
  });

  it('falls back to its own sentence when the server said nothing a shopper can read', async () => {
    service.remove = () => throwError(() => ({ status: 0 }));
    const store = opened();

    await store.remove('A');

    expect(store.problem()).toBe('addresses.failed');
  });

  /** A second press while the first is on its way is ignored, not sent twice. */
  it('sends one change at a time', async () => {
    const answer = new Subject<AddressBookView>();
    let sent = 0;
    service.add = () => (sent++, answer);
    const store = opened();
    const request = { label: 'Work', address, deliveryDefault: false, billingDefault: false };

    const first = store.add(request);
    const second = await store.add(request);
    answer.next(book(entry('B', 'Work')));
    answer.complete();

    expect([sent, second, await first, store.saving()]).toEqual([1, false, true, false]);
  });

  it('counts the book as full at its ceiling', async () => {
    service.get = () =>
      of({ entries: Array.from({ length: 10 }, (_, i) => entry(`E${i}`, `Entry ${i}`)), maxEntries: 10 });
    const store = opened();

    await store.load();

    expect(store.full()).toBe(true);
  });
});
