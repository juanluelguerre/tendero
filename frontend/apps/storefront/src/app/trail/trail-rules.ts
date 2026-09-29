import type { BrowsingKind } from '@tendero/shared-api';

/**
 * The trail's rules — *Sigue donde lo dejaste* and *Tu historial* (R-8, H-3;
 * docs/privacy.md) — as pure functions over plain values, so every rule the
 * privacy note promises is a unit test and not a comment.
 *
 * They are Accounts' rules (`BrowsingHistory`), applied to a guest's trail in the
 * browser: the same caps, the same 90 days, the same normalising of a code and of
 * a search, the same "each once, most recent first". A guest who signs in sends
 * this trail to the server, which applies them again; the two agreeing is what
 * makes the merge a union rather than a surprise.
 */

/** What is kept and what is shown. The server keeps as much for a customer. */
export const TrailLimits = {
  /** Products kept, most recent first. */
  keptViews: 50,
  /** Searches kept, most recent first. */
  keptSearches: 20,
  /** Products the home's row shows. */
  shownViews: 8,
  /** Searches the home's row shows, as chips. */
  shownSearches: 5,
  /** An entry older than this is never shown and is dropped when the trail is next written. */
  retentionDays: 90,
  /** Accounts' own limits on a key (`BrowsingHistory.MaxCodeLength`, `MaxSearchLength`). */
  maxCodeLength: 20,
  maxSearchLength: 200,
} as const;

export type TrailKind = BrowsingKind;

/** One product viewed (its public code) or one search (its words), with when. */
export interface TrailEntry {
  readonly key: string;
  /** ISO 8601, as the server answers it. */
  readonly at: string;
}

export interface TrailSnapshot {
  /** False once the shopper switched it off: nothing is shown or recorded until it is on again. */
  readonly enabled: boolean;
  /** Most recent first. */
  readonly views: readonly TrailEntry[];
  /** Most recent first. */
  readonly searches: readonly TrailEntry[];
}

export const EmptyTrail: TrailSnapshot = { enabled: true, views: [], searches: [] };

const DayMs = 24 * 60 * 60 * 1000;

/**
 * A key as Accounts stores it: a code trimmed and upper-cased, a search trimmed
 * with its spacing tidied. Empty when there is nothing to keep, or when it is
 * longer than the server would take — a key it would refuse is not one to
 * remember and then fail to merge.
 */
export function normaliseKey(kind: TrailKind, key: string): string {
  const trimmed = (key ?? '').trim();
  if (kind === 'View') {
    const code = trimmed.toUpperCase();
    return code.length <= TrailLimits.maxCodeLength ? code : '';
  }
  const words = trimmed.split(/\s+/).filter(Boolean).join(' ');
  return words.length <= TrailLimits.maxSearchLength ? words : '';
}

/** Whether two keys are the same entry: a code exactly, a search whatever its case. */
export function sameKey(kind: TrailKind, a: string, b: string): boolean {
  return kind === 'View' ? a === b : a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;
}

/** The entries still inside the 90 days, most recent first. */
export function stillKept(entries: readonly TrailEntry[], now: Date): TrailEntry[] {
  const oldest = now.getTime() - TrailLimits.retentionDays * DayMs;
  return entries
    .filter((entry) => Date.parse(entry.at) > oldest)
    .sort((a, b) => Date.parse(b.at) - Date.parse(a.at));
}

/**
 * The trail with one more entry: at the front, once (an earlier visit to the same
 * product moves up rather than doubling), capped, and nothing older than 90 days.
 * Unchanged when the trail is switched off or the key is not one to keep.
 */
export function withEntry(trail: TrailSnapshot, kind: TrailKind, key: string, now: Date): TrailSnapshot {
  const normalised = normaliseKey(kind, key);
  if (!trail.enabled || !normalised) return trail;

  const entries = kind === 'View' ? trail.views : trail.searches;
  const kept = [
    { key: normalised, at: now.toISOString() },
    ...stillKept(entries, now).filter((entry) => !sameKey(kind, entry.key, normalised)),
  ].slice(0, kind === 'View' ? TrailLimits.keptViews : TrailLimits.keptSearches);

  return kind === 'View' ? { ...trail, views: kept } : { ...trail, searches: kept };
}

/** The trail without one entry. */
export function withoutEntry(trail: TrailSnapshot, kind: TrailKind, key: string): TrailSnapshot {
  const normalised = normaliseKey(kind, key);
  const keep = (entries: readonly TrailEntry[]) => entries.filter((entry) => !sameKey(kind, entry.key, normalised));
  return kind === 'View' ? { ...trail, views: keep(trail.views) } : { ...trail, searches: keep(trail.searches) };
}

/** Switched on or off. Off forgets everything in the same change, as the server does. */
export function switched(trail: TrailSnapshot, enabled: boolean): TrailSnapshot {
  return enabled ? { ...trail, enabled: true } : { enabled: false, views: [], searches: [] };
}

/** What the home's row shows: the most recent few, within the 90 days. */
export function shownOf(trail: TrailSnapshot, now: Date): TrailSnapshot {
  return {
    enabled: trail.enabled,
    views: stillKept(trail.views, now).slice(0, TrailLimits.shownViews),
    searches: stillKept(trail.searches, now).slice(0, TrailLimits.shownSearches),
  };
}

/**
 * A trail read back from the browser's storage, which anything may have written:
 * an old version of this code, another tab, a person in the console. Whatever is
 * not an entry is dropped, the rules are applied again, and anything unreadable
 * is an empty trail rather than an error on the home page.
 */
export function readTrail(stored: string | null, now: Date): TrailSnapshot {
  if (!stored) return EmptyTrail;

  let parsed: unknown;
  try {
    parsed = JSON.parse(stored);
  } catch {
    return EmptyTrail;
  }
  if (!parsed || typeof parsed !== 'object') return EmptyTrail;

  const value = parsed as Partial<Record<keyof TrailSnapshot, unknown>>;
  const enabled = value.enabled !== false;
  if (!enabled) return switched(EmptyTrail, false);

  const entries = (kind: TrailKind, raw: unknown) => {
    const valid = (Array.isArray(raw) ? raw : [])
      .filter((entry): entry is TrailEntry =>
        !!entry && typeof entry.key === 'string' && typeof entry.at === 'string' && !Number.isNaN(Date.parse(entry.at)))
      .map((entry) => ({ key: normaliseKey(kind, entry.key), at: entry.at }))
      .filter((entry) => entry.key.length > 0);
    // Oldest first through `withEntry`'s own rule would lose the stored times, so
    // the same rule is applied here directly: once each, latest first, capped.
    const once: TrailEntry[] = [];
    for (const entry of stillKept(valid, now)) {
      if (!once.some((kept) => sameKey(kind, kept.key, entry.key))) once.push(entry);
    }
    return once.slice(0, kind === 'View' ? TrailLimits.keptViews : TrailLimits.keptSearches);
  };

  return { enabled: true, views: entries('View', value.views), searches: entries('Search', value.searches) };
}

/** Whether there is anything to show: a first visit does not see an empty box. */
export function hasTrail(trail: TrailSnapshot): boolean {
  return trail.views.length > 0 || trail.searches.length > 0;
}
