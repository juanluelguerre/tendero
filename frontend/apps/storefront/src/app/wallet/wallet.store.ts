import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import type { SavedMethodView, WalletView } from '@tendero/shared-api';
import { withAccountSection } from '../account-section/with-account-section';
import { WalletService } from '../data-access/wallet.service';

/**
 * The signed-in shopper's saved ways to pay (P15-4, ADR 0064; ADR 0047):
 * shop-wide, because the account's page and checkout (P15-5) read the same
 * wallet. As the address book does, it holds the server's answer and nothing of
 * its own (`withAccountSection`): one default, five at most, an expired card said
 * to be one, a default removed leaving none. No token is ever here.
 */
export const WalletStore = signalStore(
  { providedIn: 'root' },
  withAccountSection<WalletView>('wallet.failed'),
  withProps(() => ({ _wallet: inject(WalletService) })),
  withComputed(({ view }) => {
    const methods = computed<readonly SavedMethodView[]>(() => view()?.methods ?? []);
    const max = computed(() => view()?.maxMethods ?? 5);
    return {
      methods,
      max,
      full: computed(() => methods().length >= max()),
      /** The default, when there is one; an expired default is still the default, and checkout does not preselect it. */
      default: computed(() => methods().find((method) => method.isDefault) ?? null),
    };
  }),
  withMethods((store) => ({
    load: () => store._load(() => store._wallet.get()),
    find(id: string): SavedMethodView | null {
      return store.methods().find((method) => method.id === id) ?? null;
    },
    keep: (instrument: string, makeDefault: boolean) =>
      store._change(() => store._wallet.keep({ instrument, makeDefault })),
    remove: (id: string) => store._change(() => store._wallet.remove(id)),
    useByDefault: (id: string) => store._change(() => store._wallet.useByDefault(id)),
  })),
);
