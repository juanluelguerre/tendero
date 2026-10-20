import { inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import type { AccountRemovalView, PersonalDataExport } from '@tendero/shared-api';
import { refusalSentence } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';
import { ShopperAccountService } from '../../data-access/shopper-account.service';

interface YourDataState {
  readonly busy: 'downloading' | 'removing' | null;
  /** What removing the account came to; null until it was removed. */
  readonly removed: AccountRemovalView | null;
  readonly problem: string | null;
}

/**
 * The shopper's own data (P15-9; board 06 of the account's mock-ups; ADR 0047):
 * one download of what the shop holds, and the account removed — the answer
 * saying what was forgotten, what is kept and for how long. The page's own state.
 */
export const YourDataStore = signalStore(
  withState<YourDataState>({ busy: null, removed: null, problem: null }),
  withProps(() => ({
    _account: inject(ShopperAccountService),
    _transloco: inject(TranslocoService),
  })),
  withMethods((store) => {
    const failed = (failure: unknown): void =>
      patchState(store, {
        busy: null,
        problem:
          refusalSentence((failure as { error?: unknown }).error) || store._transloco.translate('yourData.failed'),
      });

    return {
      /** Everything the shop holds, for the page to hand to the shopper as a file; null when it could not be read. */
      async download(): Promise<PersonalDataExport | null> {
        patchState(store, { busy: 'downloading', problem: null });
        try {
          const data = await firstValueFrom(store._account.data());
          patchState(store, { busy: null });
          return data;
        } catch (failure) {
          failed(failure);
          return null;
        }
      },

      async remove(): Promise<void> {
        if (store.busy()) return;
        patchState(store, { busy: 'removing', problem: null });
        try {
          const removed = await firstValueFrom(store._account.remove());
          // Nothing is read again: this session's customer is gone, and reading would
          // link a new one. The page offers signing out, which ends the session.
          patchState(store, { busy: null, removed });
        } catch (failure) {
          failed(failure);
        }
      },
    };
  }),
);
