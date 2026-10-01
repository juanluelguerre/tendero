import type { CampaignView } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { describeCampaigns } from './campaign-tool';

describe('list_campaigns, as an agent reads it', () => {
  const now = new Date('2026-10-03T10:00:00Z');

  it('says so when nothing is near', () => {
    expect(describeCampaigns([], 'es', now)).toBe('No campaign is running or starting in the next 30 days.');
  });

  it('gives each campaign its window, time left, holds with their coupon, and its products', () => {
    const text = describeCampaigns(
      [
        campaign({ endsAt: '2026-10-07T16:12:00Z', holds: [{ name: 'Envío gratis desde 50 €', couponCode: null }] }),
        campaign({
          code: 'BLACK_FRIDAY',
          name: 'Black Friday',
          phase: 'Upcoming',
          startsAt: '2026-10-04T10:00:00Z',
          holds: [{ name: 'Black Friday', couponCode: 'BF2026' }],
        }),
      ],
      'en',
      now,
    );

    expect(text).toContain('2 campaign(s)');
    expect(text).toContain(
      'Tendero Days — code TENDERO_DAYS, event for the whole shop, running until 2026-10-07T16:12:00Z (4 d 6 h 12 min left)',
    );
    expect(text).toContain('Holds: Envío gratis desde 50 €. Its products: /en?campaign=TENDERO_DAYS');
    expect(text).toContain('starts 2026-10-04T10:00:00Z (in 1 d 0 h 0 min)');
    expect(text).toContain('Black Friday (coupon BF2026)');
  });
});

function campaign(overrides: Partial<CampaignView>): CampaignView {
  return {
    code: 'TENDERO_DAYS',
    name: 'Tendero Days',
    line: 'A week of corner-shop prices',
    kind: 'Event',
    scope: 'Shop',
    categoryCode: null,
    phase: 'Running',
    startsAt: '2026-10-01T00:00:00Z',
    endsAt: '2026-10-07T21:59:00Z',
    priority: 100,
    holds: [],
    imageId: null,
    imageAlt: null,
    ...overrides,
  };
}
