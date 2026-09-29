import { describe, expect, it } from 'vitest';
import {
  EmptyTrail,
  hasTrail,
  normaliseKey,
  readTrail,
  shownOf,
  switched,
  TrailLimits,
  withEntry,
  withoutEntry,
  type TrailEntry,
  type TrailSnapshot,
} from './trail-rules';

const now = new Date('2026-09-29T12:00:00Z');
const daysAgo = (days: number) => new Date(now.getTime() - days * 86_400_000).toISOString();

describe('the trail, as a guest keeps it', () => {
  it('puts the latest view first, and a product viewed again moves up rather than doubling', () => {
    let trail = withEntry(EmptyTrail, 'View', 'aaa', new Date(daysAgo(2)));
    trail = withEntry(trail, 'View', 'BBB', new Date(daysAgo(1)));
    trail = withEntry(trail, 'View', ' AAA ', now);

    expect(trail.views.map((entry) => entry.key)).toEqual(['AAA', 'BBB']);
    expect(trail.views[0].at).toBe(now.toISOString());
  });

  it('keeps a search once whatever its case, with its spacing tidied', () => {
    let trail = withEntry(EmptyTrail, 'Search', '  mochila   impermeable ', new Date(daysAgo(1)));
    trail = withEntry(trail, 'Search', 'Mochila Impermeable', now);

    expect(trail.searches.map((entry) => entry.key)).toEqual(['Mochila Impermeable']);
  });

  it('keeps 50 products and 20 searches, dropping the oldest', () => {
    let trail: TrailSnapshot = EmptyTrail;
    for (let i = 0; i < 60; i++) {
      trail = withEntry(trail, 'View', `P${i}`, new Date(now.getTime() + i));
      trail = withEntry(trail, 'Search', `query ${i}`, new Date(now.getTime() + i));
    }

    expect(trail.views).toHaveLength(TrailLimits.keptViews);
    expect(trail.views[0].key).toBe('P59');
    expect(trail.views[trail.views.length - 1].key).toBe('P10');
    expect(trail.searches).toHaveLength(TrailLimits.keptSearches);
  });

  it('forgets an entry after 90 days', () => {
    const trail: TrailSnapshot = {
      enabled: true,
      views: [entry('OLD', daysAgo(91)), entry('EDGE', daysAgo(89))],
      searches: [],
    };

    expect(shownOf(trail, now).views.map((item) => item.key)).toEqual(['EDGE']);
    expect(withEntry(trail, 'View', 'NEW', now).views.map((item) => item.key)).toEqual(['NEW', 'EDGE']);
  });

  it('shows the last 8 products and 5 searches', () => {
    const trail: TrailSnapshot = {
      enabled: true,
      views: Array.from({ length: 12 }, (_, i) => entry(`P${i}`, daysAgo(i))),
      searches: Array.from({ length: 9 }, (_, i) => entry(`q${i}`, daysAgo(i))),
    };

    const shown = shownOf(trail, now);
    expect(shown.views).toHaveLength(TrailLimits.shownViews);
    expect(shown.searches.map((item) => item.key)).toEqual(['q0', 'q1', 'q2', 'q3', 'q4']);
  });

  it('removes one entry', () => {
    const trail: TrailSnapshot = { enabled: true, views: [entry('AAA', daysAgo(1))], searches: [entry('Cafetera', daysAgo(1))] };

    expect(withoutEntry(trail, 'Search', 'cafetera').searches).toEqual([]);
    expect(withoutEntry(trail, 'View', 'aaa').views).toEqual([]);
  });

  it('records nothing once switched off, and switching off forgets what was kept', () => {
    const on: TrailSnapshot = { enabled: true, views: [entry('AAA', daysAgo(1))], searches: [] };
    const off = switched(on, false);

    expect(off).toEqual({ enabled: false, views: [], searches: [] });
    expect(withEntry(off, 'View', 'BBB', now)).toBe(off);
    expect(switched(off, true).enabled).toBe(true);
  });

  it('keeps nothing the server would refuse', () => {
    expect(normaliseKey('View', 'X'.repeat(21))).toBe('');
    expect(normaliseKey('Search', '   ')).toBe('');
    expect(withEntry(EmptyTrail, 'Search', '   ', now)).toBe(EmptyTrail);
  });

  it('has nothing to show on a first visit', () => {
    expect(hasTrail(EmptyTrail)).toBe(false);
  });
});

describe('the trail, read back from storage', () => {
  it('is empty when nothing or nonsense was stored', () => {
    expect(readTrail(null, now)).toEqual(EmptyTrail);
    expect(readTrail('{not json', now)).toEqual(EmptyTrail);
    expect(readTrail('42', now)).toEqual(EmptyTrail);
  });

  it('drops what is not an entry, and applies the rules again', () => {
    const stored = JSON.stringify({
      enabled: true,
      views: [{ key: 'aaa', at: daysAgo(1) }, { key: 7 }, { key: 'OLD', at: daysAgo(120) }, { key: 'AAA', at: daysAgo(3) }],
      searches: 'nope',
    });

    expect(readTrail(stored, now)).toEqual({ enabled: true, views: [entry('AAA', daysAgo(1))], searches: [] });
  });

  it('remembers that it was switched off', () => {
    expect(readTrail(JSON.stringify({ enabled: false, views: [entry('AAA', daysAgo(1))] }), now)).toEqual({
      enabled: false,
      views: [],
      searches: [],
    });
  });
});

function entry(key: string, at: string): TrailEntry {
  return { key, at };
}
