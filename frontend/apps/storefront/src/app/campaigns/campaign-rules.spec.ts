import type { CampaignView } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import {
  campaignForCategory,
  clockTarget,
  holdLabels,
  leadingCampaign,
  momentOf,
  nextChangeIn,
  spokenTimeLeft,
  timeLeft,
  withAncestors,
} from './campaign-rules';

describe('which campaign a page draws', () => {
  it('leads the home with the first shop-wide campaign, in the server order', () => {
    const list = [
      campaign({ code: 'KITCHEN_WEEK', scope: 'Category', categoryCode: 'KITCHEN' }),
      campaign({ code: 'OTONO', kind: 'Season' }),
      campaign({ code: 'TENDERO_DAYS', phase: 'Upcoming' }),
    ];

    expect(leadingCampaign(list)?.code).toBe('OTONO');
  });

  it('draws nothing when no shop-wide campaign is near', () => {
    expect(leadingCampaign([])).toBeNull();
    expect(leadingCampaign([campaign({ scope: 'Category', categoryCode: 'HOME' })])).toBeNull();
  });

  it('skips a phase it does not draw, rather than showing a campaign that is over', () => {
    expect(leadingCampaign([campaign({ code: 'OLD', phase: 'Over' }), campaign({ code: 'NEW' })])?.code).toBe('NEW');
  });

  it('gives a category the campaign of its own branch, the nearest first', () => {
    const list = [
      campaign({ code: 'SHOP' }),
      campaign({ code: 'HOME_WEEK', scope: 'Category', categoryCode: 'HOME' }),
    ];

    expect(campaignForCategory(list, ['KITCHEN', 'HOME'])?.code).toBe('HOME_WEEK');
    expect(campaignForCategory(list, ['APPAREL'])).toBeNull();
  });

  it('walks a category up to its department', () => {
    const tree = [
      { code: 'HOME', parent: null },
      { code: 'KITCHEN', parent: 'HOME' },
      { code: 'COFFEE_MAKER', parent: 'KITCHEN' },
    ];

    expect(withAncestors('COFFEE_MAKER', tree)).toEqual(['COFFEE_MAKER', 'KITCHEN', 'HOME']);
    expect(withAncestors('UNKNOWN', tree)).toEqual(['UNKNOWN']);
  });
});

describe('the clock', () => {
  const now = new Date('2026-10-03T10:00:00Z');

  it('counts to the end while a campaign runs and to the start before it opens', () => {
    const running = campaign({ phase: 'Running' });
    const upcoming = campaign({ phase: 'Upcoming' });

    expect(clockTarget(running)).toBe(running.endsAt);
    expect(momentOf(running)).toBe('during');
    expect(clockTarget(upcoming)).toBe(upcoming.startsAt);
    expect(momentOf(upcoming)).toBe('before');
  });

  it('has no clock for a season', () => {
    expect(clockTarget(campaign({ kind: 'Season' }))).toBeNull();
  });

  it('says whole days, hours and minutes, rounded down', () => {
    expect(timeLeft('2026-10-07T16:12:59Z', now)).toEqual({ days: 4, hours: 6, minutes: 12, over: false });
    expect(timeLeft('2026-10-03T10:00:59Z', now)).toEqual({ days: 0, hours: 0, minutes: 0, over: false });
  });

  it('stops at zero once the moment has passed', () => {
    expect(timeLeft('2026-10-03T10:00:00Z', now)).toEqual({ days: 0, hours: 0, minutes: 0, over: true });
    expect(timeLeft('2026-10-01T00:00:00Z', now).over).toBe(true);
  });

  it('wakes on the next whole minute, never sooner', () => {
    expect(nextChangeIn('2026-10-03T10:05:20Z', now)).toBe(20_000);
    expect(nextChangeIn('2026-10-03T10:05:00Z', now)).toBe(60_000);
    expect(nextChangeIn('2026-10-03T09:00:00Z', now)).toBe(0);
  });

  it('is spoken as a sentence with the language own plurals', () => {
    expect(spokenTimeLeft({ days: 4, hours: 6, minutes: 12, over: false }, 'es')).toBe('4 días, 6 horas y 12 minutos');
    expect(spokenTimeLeft({ days: 1, hours: 1, minutes: 1, over: false }, 'en')).toBe('1 day, 1 hour, and 1 minute');
    expect(spokenTimeLeft({ days: 0, hours: 0, minutes: 5, over: false }, 'es')).toBe('5 minutos');
  });
});

describe('what a campaign holds', () => {
  it('names a coupon promotion with its code, and the others by name alone', () => {
    const labels = holdLabels(
      campaign({ holds: [{ name: 'Black Friday', couponCode: 'BF2026' }, { name: 'Envío gratis desde 50 €', couponCode: null }] }),
      (name, code) => `${name} con el código ${code}`,
    );

    expect(labels).toEqual(['Black Friday con el código BF2026', 'Envío gratis desde 50 €']);
  });
});

function campaign(overrides: Partial<CampaignView>): CampaignView {
  return {
    code: 'TENDERO_DAYS',
    name: 'Tendero Days',
    line: 'Una semana de precios de tienda de barrio',
    kind: 'Event',
    scope: 'Shop',
    categoryCode: null,
    phase: 'Running',
    startsAt: '2026-10-01T00:00:00+02:00',
    endsAt: '2026-10-07T23:59:00+02:00',
    priority: 100,
    holds: [],
    imageId: null,
    imageAlt: null,
    ...overrides,
  };
}
