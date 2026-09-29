import { computed, inject, Injectable } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import type { CampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { CampaignService } from '../data-access/campaign.service';
import { leadingCampaign } from './campaign-rules';

/**
 * The campaigns near now, read once per language and shared by everything that
 * draws one: the home's card, a category's banner, a campaign listing's heading
 * and its chip (R-8). Four readers of one answer, as the taxonomy is read once for
 * the menu and the drawer.
 *
 * **A campaign is never worth an error on a page.** Loading, failed and none near
 * all read as an empty list, and a page with no campaign draws nothing where the
 * campaign would be.
 */
@Injectable({ providedIn: 'root' })
export class Campaigns {
  private readonly service = inject(CampaignService);
  private readonly culture = inject(CultureStore);

  private readonly list = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) => this.service.list(params.culture),
  });

  /** In the server's order: the leading one first. */
  readonly all = computed<readonly CampaignView[]>(() => (this.list.hasValue() ? this.list.value().items : []));

  /** Whether the list has answered, either way — a page waits for it before saying a campaign is unknown. */
  readonly settled = computed(() => !this.list.isLoading());

  /** The one the home leads with, or null. */
  readonly leading = computed(() => leadingCampaign(this.all()));

  named(code: string): CampaignView | null {
    return this.all().find((campaign) => campaign.code.toUpperCase() === code.toUpperCase()) ?? null;
  }

  /** Asked again when a clock reaches zero: the phase has changed, and the server says what leads now. */
  refresh(): void {
    this.list.reload();
  }
}
