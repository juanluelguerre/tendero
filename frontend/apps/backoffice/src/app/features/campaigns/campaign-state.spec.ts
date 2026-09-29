import { describe, expect, it } from 'vitest';
import { bucketOf, inCulture, windowOf } from './campaign-state';

/** Where a campaign sits in the calendar: the stored status and the window's phase, read together (ADR 0044). */
describe('which chip a campaign falls under', () => {
  it.each([
    ['Scheduled', 'Running', 'running'],
    ['Scheduled', 'Upcoming', 'scheduled'],
    ['Draft', 'Upcoming', 'drafts'],
    ['Draft', 'Running', 'drafts'],
    ['Ended', 'Upcoming', 'ended'],
    ['Ended', 'Running', 'ended'],
    ['Scheduled', 'Over', 'ended'],
    ['Draft', 'Over', 'ended'],
  ] as const)('%s and %s is %s', (status, phase, bucket) => {
    expect(bucketOf({ status, phase })).toBe(bucket);
  });
});

describe("a campaign's words", () => {
  it('are the reader’s language, and English when that one is empty', () => {
    expect(inCulture({ es: 'Otoño en Tendero', en: 'Autumn at Tendero' }, 'es')).toBe('Otoño en Tendero');
    expect(inCulture({ es: 'Otoño en Tendero', en: 'Autumn at Tendero' }, 'en')).toBe('Autumn at Tendero');
    expect(inCulture({ es: '  ', en: 'Autumn at Tendero' }, 'es')).toBe('Autumn at Tendero');
  });
});

describe("a campaign's window, as the list shows it", () => {
  const now = new Date('2026-09-29T10:00:00Z');

  it('is compact within a month, and says the month once', () => {
    const shown = windowOf('2026-10-01T00:00:00Z', '2026-10-07T23:59:00Z', 'es', now, 'UTC');

    expect(shown).toMatch(/^1\s?[–-]\s?7 oct/);
    expect(shown).not.toMatch(/2026/);
  });

  it('names both months when it crosses one, in the reader’s culture', () => {
    expect(windowOf('2026-09-22T00:00:00Z', '2026-11-30T23:59:00Z', 'es', now, 'UTC')).toMatch(/^22 sept?\.?\s[–-]\s30 nov/);
    expect(windowOf('2026-09-22T00:00:00Z', '2026-11-30T23:59:00Z', 'en', now, 'UTC')).toMatch(/^Sep 22\s?[–-]\s?Nov 30/);
  });

  it('says the year only when it is needed', () => {
    expect(windowOf('2026-12-20T00:00:00Z', '2027-01-06T23:59:00Z', 'es', now, 'UTC')).toMatch(/2026.*2027/);
    expect(windowOf('2027-03-01T00:00:00Z', '2027-03-07T23:59:00Z', 'es', now, 'UTC')).toMatch(/2027/);
  });
});
