import type { CampaignView } from '@tendero/shared-api';
import { timeLeft } from '../campaigns/campaign-rules';

/**
 * What `list_campaigns` answers a browser agent (R-8, H-5): "which events are on
 * or coming, until when, and what do they hold" — so an agent asked "are there
 * deals this week?" does not have to scrape a card.
 *
 * Pure, over the same list the page draws, in the server's order. Dates as ISO
 * 8601 and times left in whole units: a model reads both without guessing a
 * locale. A coupon's code is data here as it is on the card (ADR 0044).
 */
export function describeCampaigns(campaigns: readonly CampaignView[], culture: string, now: Date): string {
  const live = campaigns.filter((campaign) => campaign.phase === 'Running' || campaign.phase === 'Upcoming');
  if (live.length === 0) return 'No campaign is running or starting in the next 30 days.';

  const lines = live.map((campaign) => {
    const where = campaign.scope === 'Category' ? `category ${campaign.categoryCode}` : 'the whole shop';
    const when =
      campaign.phase === 'Running'
        ? `running until ${campaign.endsAt} (${left(campaign.endsAt, now)} left)`
        : `starts ${campaign.startsAt} (in ${left(campaign.startsAt, now)}), ends ${campaign.endsAt}`;
    const holds = campaign.holds.length
      ? campaign.holds
          .map((hold) => (hold.couponCode ? `${hold.name} (coupon ${hold.couponCode})` : hold.name))
          .join('; ')
      : 'nothing yet';

    return (
      `${campaign.name} — code ${campaign.code}, ${campaign.kind.toLowerCase()} for ${where}, ${when}. ` +
      `${campaign.line}. Holds: ${holds}. Its products: /${culture}?campaign=${campaign.code}`
    );
  });

  return `${live.length} campaign(s), the one the shop leads with first:\n${lines.join('\n')}`;
}

function left(until: string, now: Date): string {
  const time = timeLeft(until, now);
  return `${time.days} d ${time.hours} h ${time.minutes} min`;
}
