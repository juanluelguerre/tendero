import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';

/**
 * The campaign the widget measures: the running one the shop puts first — the
 * highest priority, as the storefront leads with — or, with none running, the
 * last that ended. A draft was never on the shop's windows; an upcoming one has
 * nothing to measure yet.
 */
export function leadingCampaign(campaigns: readonly ManagedCampaignView[]): ManagedCampaignView | null {
  // Ended before it opened, a campaign never ran: there is nothing to measure.
  const live = campaigns.filter((campaign) => campaign.status !== 'Draft' && instant(closesAt(campaign)) > instant(campaign.startsAt));
  const running = live.filter((campaign) => campaign.phase === 'Running').sort((a, b) => b.priority - a.priority);
  if (running.length > 0) return running[0];
  const over = live.filter((campaign) => campaign.phase === 'Over').sort((a, b) => instant(closesAt(b)) - instant(closesAt(a)));
  return over[0] ?? null;
}

/** A campaign ended early closes its window when it ended, not when it was meant to. */
function closesAt(campaign: ManagedCampaignView): string {
  return campaign.endedAt && instant(campaign.endedAt) < instant(campaign.endsAt) ? campaign.endedAt : campaign.endsAt;
}

/** Instants compared as instants: two ISO strings in different offsets do not sort as text. */
function instant(at: string): number {
  return new Date(at).getTime();
}

/**
 * Campaigns' state (ADR 0047): the campaign the widget measures, from Pricing's
 * calendar, and its days against the same weekday over the four weeks before it
 * opened, from Ordering (R-9, D-7). Dates in, figures out: the window is asked
 * as Pricing gives it, and a category's campaign on its category only.
 */
export const CampaignsStore = signalStore(
  withProps(() => {
    const sources = inject(DashboardSources);
    return { _sources: sources, _calendar: refreshedResource({ params: () => ({}), stream: (_, at) => sources.campaigns(at) }) };
  }),
  withComputed((store) => ({
    campaign: computed(() => (store._calendar.hasValue() ? leadingCampaign(store._calendar.value()) : null)),
  })),
  withProps((store) => ({
    uplift: refreshedResource({
      params: () => {
        const campaign = store.campaign();
        return campaign ? { from: campaign.startsAt, to: closesAt(campaign), category: campaign.scope === 'Category' ? campaign.categoryCode : null } : undefined;
      },
      stream: (params, at) => store._sources.uplift(params.from, params.to, params.category, at),
    }),
  })),
  withComputed((store) => ({
    /** Waiting for the calendar or the figures, and nothing drawn yet. */
    loading: computed(() => (store._calendar.isLoading() && !store._calendar.hasValue()) || (store.uplift.isLoading() && !store.uplift.hasValue())),
    failed: computed(() => !!store._calendar.error() || !!store.uplift.error()),
  })),
);
