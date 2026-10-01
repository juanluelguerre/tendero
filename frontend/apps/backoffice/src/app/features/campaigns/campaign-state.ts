import type { BilingualText, CampaignPhase, CampaignStatus } from '@tendero/shared-api';

/**
 * Where a campaign sits in the shopkeeper's calendar: the chips of the list, in
 * the order a working day asks about them.
 *
 * Neither the stored status nor the phase alone answers it (ADR 0044): the
 * server stores Draft, Scheduled or Ended, and computes the phase from the
 * window when read. A scheduled campaign whose window is open is running; one
 * whose window has not opened is upcoming; and anything whose window has
 * closed is over, whatever it was stored as — a draft left past its dates can
 * no longer be scheduled, so it is among the ended and not among the work.
 */
export type CampaignBucket = 'running' | 'scheduled' | 'drafts' | 'ended';

export const CampaignBuckets: readonly CampaignBucket[] = ['running', 'scheduled', 'drafts', 'ended'];

export function bucketOf(campaign: { readonly status: CampaignStatus; readonly phase: CampaignPhase }): CampaignBucket {
  if (campaign.status === 'Ended' || campaign.phase === 'Over') return 'ended';
  if (campaign.status === 'Draft') return 'drafts';
  return campaign.phase === 'Running' ? 'running' : 'scheduled';
}

/** A campaign's words in the reader's culture, English when that one is empty (invariant 6's order). */
export function inCulture(text: BilingualText, culture: string): string {
  const wanted = culture === 'es' ? text.es : text.en;
  return wanted.trim() ? wanted : text.en || text.es;
}

/**
 * A campaign's window as the list shows it, compact: `1–7 oct`, `22 sep – 30
 * nov`. The year is said only when it is needed — the window crosses one, or
 * is not in this one — because a calendar of this season's campaigns reads
 * the same year on every row.
 */
export function windowOf(
  startsAt: string,
  endsAt: string,
  culture: string,
  now: Date = new Date(),
  timeZone?: string,
): string {
  const start = new Date(startsAt);
  const end = new Date(endsAt);
  const year = (instant: Date) => Number(new Intl.DateTimeFormat('en', { year: 'numeric', timeZone }).format(instant));
  const withYear = year(start) !== year(end) || year(start) !== year(now);

  const format = new Intl.DateTimeFormat(culture, {
    day: 'numeric',
    month: 'short',
    ...(withYear ? { year: 'numeric' } : {}),
    timeZone,
  }) as Intl.DateTimeFormat & RangeFormat;
  // Every browser the backoffice supports has had it since 2021; the project's
  // `lib` predates its type. It collapses what the two ends share: `1–7 oct`.
  return format.formatRange(start, end);
}

/** `Intl.DateTimeFormat.prototype.formatRange` (ES2021), which the project's `lib` does not declare yet. */
interface RangeFormat {
  formatRange(start: Date, end: Date): string;
}
