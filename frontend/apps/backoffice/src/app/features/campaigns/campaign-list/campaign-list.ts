import { NgTemplateOutlet } from '@angular/common';
import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { FilterChips } from '../../../worklist/filter-chips/filter-chips';
import { ListState } from '../../../worklist/list-state/list-state';
import { bucketOf, type CampaignBucket, inCulture, windowOf } from '../campaign-state';
import { CampaignsStore } from '../campaigns.store';

/**
 * The campaign calendar as a list (ADR 0042): by what they are doing on the
 * shop's windows — running, scheduled, drafts, ended — each with its kind, its
 * scope and its dates, and New while no pane holds the view's one action. It
 * reads and acts through the page's store, which the editor shares.
 */
@Component({
  selector: 'backoffice-campaign-list',
  imports: [TranslocoDirective, NgTemplateOutlet, FilterChips, ListState],
  templateUrl: './campaign-list.html',
})
export class CampaignList {
  protected readonly store = inject(CampaignsStore);
  private readonly culture = inject(CultureStore);

  protected nameOf(campaign: ManagedCampaignView): string {
    return inCulture(campaign.name, this.culture.active());
  }

  protected categoryOf(campaign: ManagedCampaignView): string | null {
    if (campaign.scope !== 'Category' || !campaign.categoryCode) return null;
    return this.store.categoryNames().get(campaign.categoryCode) ?? campaign.categoryCode;
  }

  protected windowOf(campaign: ManagedCampaignView): string {
    return windowOf(campaign.startsAt, campaign.endsAt, this.culture.active());
  }

  protected bucketOf(campaign: ManagedCampaignView): CampaignBucket {
    return bucketOf(campaign);
  }
}
