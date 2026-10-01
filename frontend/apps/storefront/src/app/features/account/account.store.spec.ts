import { TestBed } from '@angular/core/testing';
import { AuthStore } from '@tendero/shared-auth';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CartStore } from '../../cart/cart.store';
import { AccountService } from '../../data-access/account.service';
import { CustomerLink } from '../../data-access/customer-link';
import { AccountStore } from './account.store';

/** A shopper's account (ADR 0047): who they are to the shop, the basket they carried in, and their orders. */
describe('the account store', () => {
  let signedIn: boolean;
  let token: string | null;
  let calls: string[];
  let link: () => Promise<void>;
  let claim: () => Promise<unknown>;

  beforeEach(() => {
    signedIn = true;
    token = 'T-1';
    calls = [];
    link = async () => void calls.push('link');
    claim = async () => void calls.push('claim');
  });

  async function opened() {
    TestBed.configureTestingModule({
      providers: [
        AccountStore,
        {
          provide: AuthStore,
          useValue: {
            isSignedIn: () => signedIn,
            signIn: () => calls.push('signIn'),
            signOut: () => calls.push('signOut'),
          },
        },
        { provide: CustomerLink, useValue: { ensure: () => link() } },
        { provide: CartStore, useValue: { token: () => token, load: async () => void calls.push('load') } },
        {
          provide: AccountService,
          useValue: {
            claimCart: () => claim(),
            orders: () => (calls.push('orders'), of({ orders: [{ orderId: 'O-1' }] })),
          },
        },
      ],
    });
    const store = TestBed.inject(AccountStore);
    await store.start();
    return store;
  }

  /** The customer first — a cart cannot be attached to somebody who does not exist yet — then the basket, then the orders. */
  it('links the customer, claims the basket carried in, then lists the orders', async () => {
    const store = await opened();

    expect([calls, store.state()]).toEqual([
      ['link', 'claim', 'load', 'orders'],
      { status: 'signedIn', orders: [{ orderId: 'O-1' }] },
    ]);
  });

  /** A basket that is another account's comes back 409; refusing to sign somebody in over it would be absurd. */
  it('signs in although the basket belongs to someone else', async () => {
    claim = () => Promise.reject({ status: 409 });

    expect((await opened()).state().status).toBe('signedIn');
  });

  it('says the account could not be reached when the customer cannot be linked', async () => {
    link = () => Promise.reject(new Error('down'));

    expect((await opened()).state().status).toBe('failed');
  });

  it('is signed out, asking for nothing, without a session', async () => {
    signedIn = false;
    const store = await opened();

    expect([store.state().status, calls]).toEqual(['signedOut', []]);
  });
});
