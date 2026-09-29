import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import type { CampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { CampaignService } from '../data-access/campaign.service';
import { leadingCampaign } from './campaign-rules';

/**
 * The campaigns near now, read once per language and shared by everything that
 * draws one: the home's card, a category's banner, a campaign listing's heading
 * and its chip (R-8; ADR 0047). Four readers of one answer, as the taxonomy is
 * read once for the menu and the drawer.
 *
 * **A campaign is never worth an error on a page.** Loading, failed and none near
 * all read as an empty list, and a page with no campaign draws nothing where the
 * campaign would be.
 */
export const CampaignsStore = signalStore(
  { providedIn: 'root' },
  withProps(() => {
    const service = inject(CampaignService);
    const culture = inject(CultureStore);
    return {
      _list: rxResource({
        params: () => ({ culture: culture.active() }),
        stream: ({ params }) => service.list(params.culture),
      }),
    };
  }),
  withComputed(({ _list }) => {
    /** In the server's order: the leading one first. */
    const all = computed<readonly CampaignView[]>(() => (_list.hasValue() ? _list.value().items : []));
    return {
      all,
      /** Whether the list has answered, either way — a page waits for it before saying a campaign is unknown. */
      settled: computed(() => !_list.isLoading()),
      /** The one the home leads with, or null. */
      leading: computed(() => leadingCampaign(all())),
    };
  }),
  withMethods((store) => ({
    named(code: string): CampaignView | null {
      return store.all().find((campaign) => campaign.code.toUpperCase() === code.toUpperCase()) ?? null;
    },
    /** Asked again when a clock reaches zero: the phase has changed, and the server says what leads now. */
    refresh(): void {
      store._list.reload();
    },
  })),
);
