import type { CampaignView } from '@tendero/shared-api';

/**
 * What the storefront decides about campaigns (R-8, ADR 0044), as pure functions.
 *
 * Very little, on purpose. **Which campaign leads is the server's order**
 * (running before upcoming, then priority, then start); the page only picks the
 * first one that belongs where it is drawing — the shop's home or one category.
 * What the page does own is the clock, because a cached answer must never count
 * down wrong: it is computed here, from the window, against the browser's now.
 */

/** Before the window opens or while it runs — the tracking plan's words for the two phases. */
export type CampaignMoment = 'before' | 'during';

/** Whether a campaign on the wire is one to draw: the public list only answers these two, and a client keeps a branch for a value it does not know (ADR 0043). */
function live(campaign: CampaignView): boolean {
  return campaign.phase === 'Running' || campaign.phase === 'Upcoming';
}

/**
 * The campaign the home leads with: the first shop-wide one in the server's
 * order. A category's campaign never reaches the home (the spec: one headline for
 * the shop, the others where they apply), and with none near nothing is drawn.
 */
export function leadingCampaign(campaigns: readonly CampaignView[]): CampaignView | null {
  return campaigns.find((campaign) => campaign.scope === 'Shop' && live(campaign)) ?? null;
}

/**
 * The banner of a category page: the first campaign scoped to this category or
 * to one above it — *Semana del hogar* on Hogar is also the banner of Cocina.
 * `codes` is the category and its ancestors, nearest first.
 */
export function campaignForCategory(campaigns: readonly CampaignView[], codes: readonly string[]): CampaignView | null {
  const wanted = new Set(codes.map((code) => code.toUpperCase()));
  return (
    campaigns.find(
      (campaign) =>
        campaign.scope === 'Category' && live(campaign) && wanted.has((campaign.categoryCode ?? '').toUpperCase()),
    ) ?? null
  );
}

/** A category and every category above it, nearest first, from the taxonomy's parent links. */
export function withAncestors(code: string, categories: readonly { code: string; parent?: string | null }[]): string[] {
  const chain: string[] = [];
  let current: string | null | undefined = code;
  while (current && !chain.includes(current)) {
    chain.push(current);
    const at: string = current;
    current = categories.find((category) => category.code === at)?.parent;
  }
  return chain;
}

export function momentOf(campaign: CampaignView): CampaignMoment {
  return campaign.phase === 'Upcoming' ? 'before' : 'during';
}

/**
 * What the clock counts to: the start before it opens, the end while it runs.
 * A season has no clock — weeks long, it is not news that it ends (the spec).
 */
export function clockTarget(campaign: CampaignView): string | null {
  if (campaign.kind === 'Season') return null;
  return momentOf(campaign) === 'before' ? campaign.startsAt : campaign.endsAt;
}

export interface TimeLeft {
  readonly days: number;
  readonly hours: number;
  readonly minutes: number;
  /** The moment has come: the clock stops at zero and the list is asked again. */
  readonly over: boolean;
}

const MinuteMs = 60_000;

/**
 * Whole days, hours and minutes to a moment, rounded down — *Termina en 4 d 06 h
 * 12 min* — and zero once it has passed. Minutes, not seconds: a clock that
 * changes every second is a distraction on a shop's front page, and the mock-ups
 * stop at the minute.
 */
export function timeLeft(until: string | Date, now: Date): TimeLeft {
  const remaining = new Date(until).getTime() - now.getTime();
  if (!(remaining > 0)) return { days: 0, hours: 0, minutes: 0, over: true };

  const minutes = Math.floor(remaining / MinuteMs);
  return {
    days: Math.floor(minutes / (24 * 60)),
    hours: Math.floor(minutes / 60) % 24,
    minutes: minutes % 60,
    over: false,
  };
}

/**
 * How long until the display next changes: to the next whole minute of the time
 * left, so the clock wakes once a minute and on the minute, never every second.
 */
export function nextChangeIn(until: string | Date, now: Date): number {
  const remaining = new Date(until).getTime() - now.getTime();
  if (!(remaining > 0)) return 0;
  return remaining % MinuteMs || MinuteMs;
}

/**
 * The time left as a screen reader should hear it, once: "4 días, 6 horas y 12
 * minutos". The platform's own plural rules (`Intl`), so there is no "1 días".
 */
export function spokenTimeLeft(left: TimeLeft, culture: string): string {
  const unit = (value: number, name: 'day' | 'hour' | 'minute') =>
    new Intl.NumberFormat(culture, { style: 'unit', unit: name, unitDisplay: 'long' }).format(value);

  const parts = [
    left.days > 0 ? unit(left.days, 'day') : null,
    left.days > 0 || left.hours > 0 ? unit(left.hours, 'hour') : null,
    unit(left.minutes, 'minute'),
  ].filter((part): part is string => part !== null);

  return new Intl.ListFormat(culture, { style: 'long', type: 'conjunction' }).format(parts);
}

/**
 * What a campaign holds, as its chips: the promotion's own name, and for one
 * behind a coupon the code as data — *con el código BF2026* — because a price
 * that needs a code is shown, never counted (ADR 0044). The sentence around the
 * code is the caller's, in the reader's language.
 */
export function holdLabels(campaign: CampaignView, withCode: (name: string, code: string) => string): string[] {
  return campaign.holds.map((hold) => (hold.couponCode ? withCode(hold.name, hold.couponCode) : hold.name));
}

/**
 * The window in the reader's own words: "1–7 de octubre", "1 Oct – 7 Oct". The
 * platform's range formatting, which knows to say the month once.
 */
export function windowOf(campaign: CampaignView, culture: string): string {
  const format = new Intl.DateTimeFormat(culture, { day: 'numeric', month: 'long' });
  return format.formatRange(new Date(campaign.startsAt), new Date(campaign.endsAt));
}

/** One day of the window in the reader's words: "30 de noviembre". */
export function dayOf(instant: string, culture: string): string {
  return new Intl.DateTimeFormat(culture, { day: 'numeric', month: 'long' }).format(new Date(instant));
}
