import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { ShopperAccountService } from '../../data-access/shopper-account.service';
import { YourDataStore } from './your-data.store';

/** The shopper's own data (P15-9): one download, and the account removed with what it came to. */
describe('the your-data store', () => {
  let removing: () => unknown;

  beforeEach(() => {
    removing = () => of({ forgotten: ['account', 'addresses'], kept: ['orders'], keptForYears: 6 });
  });

  function opened() {
    TestBed.configureTestingModule({
      providers: [
        YourDataStore,
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        {
          provide: ShopperAccountService,
          useValue: {
            data: () => of({ generatedAt: '2026-10-21T10:00:00Z', account: {}, sections: {} }),
            remove: () => removing(),
          },
        },
      ],
    });
    return TestBed.inject(YourDataStore);
  }

  it('hands back everything the shop holds for the page to save', async () => {
    const store = opened();

    const data = await store.download();

    expect([data?.generatedAt, store.busy()]).toEqual(['2026-10-21T10:00:00Z', null]);
  });

  it('says what removing the account came to: forgotten, kept and for how long', async () => {
    const store = opened();

    await store.remove();

    expect(store.removed()).toEqual({ forgotten: ['account', 'addresses'], kept: ['orders'], keptForYears: 6 });
  });

  it('keeps the account when removing failed, and says so in the server’s words', async () => {
    removing = () =>
      throwError(() => ({ status: 409, error: { code: 'NoCustomer', title: 'Tu cuenta todavía no está lista.' } }));
    const store = opened();

    await store.remove();

    expect([store.removed(), store.problem(), store.busy()]).toEqual([null, 'Tu cuenta todavía no está lista.', null]);
  });
});
