import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import type { SavedMethodView, WalletView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CustomerLink } from '../data-access/customer-link';
import { WalletService } from '../data-access/wallet.service';
import { WalletStore } from './wallet.store';

const method = (id: string, last4: string, isDefault = false, expired = false): SavedMethodView => ({
  id,
  brand: 'Visa',
  last4,
  expiryMonth: 8,
  expiryYear: 2028,
  expired,
  isDefault,
});

const wallet = (...methods: SavedMethodView[]): WalletView => ({ methods, maxMethods: 5 });

/** The shopper's saved ways to pay (P15-4): the server's wallet is the only one, and no token is ever in it. */
describe('the wallet store', () => {
  let kept: unknown[];

  beforeEach(() => (kept = []));

  function opened(read: WalletView) {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthStore, useValue: { isSignedIn: () => true } },
        { provide: CustomerLink, useValue: { ensure: async () => undefined } },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        {
          provide: WalletService,
          useValue: {
            get: () => of(read),
            keep: (card: unknown) => (kept.push(card), of(wallet(method('A', '4242', true), method('B', '4444')))),
          },
        },
      ],
    });
    return TestBed.inject(WalletStore);
  }

  it('reads the wallet and its default once the customer is linked', async () => {
    const store = opened(wallet(method('A', '4242'), method('B', '4444', true)));

    await store.load();

    expect([store.status(), store.default()?.last4, store.full()]).toEqual(['ready', '4444', false]);
  });

  /** What is sent is what the provider's field produced and whether it is the default: never a number. */
  it('keeps a card from its one-time instrument and shows the wallet the server answered', async () => {
    const store = opened(wallet());
    await store.load();

    expect(await store.keep('card-ok', true)).toBe(true);

    expect(kept).toEqual([{ instrument: 'card-ok', makeDefault: true }]);
    expect(store.methods().map((each) => each.last4)).toEqual(['4242', '4444']);
  });

  it('counts the wallet as full at its ceiling', async () => {
    const store = opened(wallet(...['1', '2', '3', '4', '5'].map((id) => method(id, `000${id}`))));

    await store.load();

    expect(store.full()).toBe(true);
  });
});
