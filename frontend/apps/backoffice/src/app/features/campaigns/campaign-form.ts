import type {
  BilingualText,
  CampaignDetailsRequest,
  CampaignKind,
  CreateCampaignRequest,
  ManagedCampaignView,
} from '@tendero/shared-api';

/**
 * What the pane edits: a campaign's details as the form holds them, the dates
 * as a `datetime-local` field reads and writes them (the shopkeeper's own
 * clock, no zone), and the category empty for the whole shop. Pure, so the
 * mapping both ways is tested without a browser.
 */
export interface CampaignForm {
  readonly code: string;
  readonly name: BilingualText;
  readonly line: BilingualText;
  readonly kind: CampaignKind;
  /** Null is the whole shop. */
  readonly categoryCode: string | null;
  /** `YYYY-MM-DDTHH:mm`, local. */
  readonly startsAt: string;
  readonly endsAt: string;
  readonly priority: number;
  readonly promotionCodes: readonly string[];
  readonly image: { readonly imageId: string; readonly alt: BilingualText } | null;
}

export const CampaignKinds: readonly CampaignKind[] = ['Event', 'Season'];

/** The server's own limit (`CampaignCode.MaxLength`), so a derived code is never refused for its length. */
const MaxCodeLength = 40;

const Blank: BilingualText = { es: '', en: '' };

/**
 * A new campaign: an event from tomorrow at midnight to the end of the
 * seventh day, the shape of *Tendero Days*. A window that opens in the future
 * is one the server accepts as it stands.
 */
export function blankForm(now: Date = new Date()): CampaignForm {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1, 0, 0);
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 7, 23, 59);
  return {
    code: '',
    name: Blank,
    line: Blank,
    kind: 'Event',
    categoryCode: null,
    startsAt: localInputOf(start),
    endsAt: localInputOf(end),
    priority: 10,
    promotionCodes: [],
    image: null,
  };
}

export function formOf(campaign: ManagedCampaignView): CampaignForm {
  return {
    code: campaign.code,
    name: campaign.name,
    line: campaign.line,
    kind: campaign.kind,
    categoryCode: campaign.scope === 'Category' ? campaign.categoryCode : null,
    startsAt: toLocalInput(campaign.startsAt),
    endsAt: toLocalInput(campaign.endsAt),
    priority: campaign.priority,
    promotionCodes: campaign.promotionCodes,
    image: campaign.image ? { imageId: campaign.image.imageId, alt: campaign.image.alt } : null,
  };
}

/** The request the form stands for: words trimmed, dates as instants, no category for the shop. */
export function detailsOf(form: CampaignForm): CampaignDetailsRequest {
  return {
    name: trimmed(form.name),
    line: trimmed(form.line),
    kind: form.kind,
    categoryCode: form.categoryCode || null,
    startsAt: fromLocalInput(form.startsAt),
    endsAt: fromLocalInput(form.endsAt),
    priority: form.priority,
    promotionCodes: [...form.promotionCodes],
    image: form.image ? { imageId: form.image.imageId, alt: trimmed(form.image.alt) } : null,
  };
}

export function createRequestOf(form: CampaignForm): CreateCampaignRequest {
  return { code: form.code.trim(), details: detailsOf(form) };
}

/** Whether the form says something the saved campaign does not: what Save is for, and what Schedule waits on. */
export function differs(form: CampaignForm, saved: CampaignForm): boolean {
  return JSON.stringify(detailsOf(form)) !== JSON.stringify(detailsOf(saved));
}

/**
 * A campaign's code from its name, as the server wants it: upper snake case,
 * a letter first, at most forty characters — *Semana del hogar* is
 * `SEMANA_DEL_HOGAR`, *Otoño* is `OTONO`. Only a suggestion: the field stays
 * editable until the campaign exists, and the server has the last word.
 */
export function codeFrom(name: string): string {
  const snake = name
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
  const lettered = /^[A-Z]/.test(snake) || snake === '' ? snake : `C_${snake}`;
  return lettered.slice(0, MaxCodeLength).replace(/_+$/, '');
}

/** The promotions with one more or one fewer: a checkbox's answer. */
export function toggled(codes: readonly string[], code: string, on: boolean): string[] {
  const without = codes.filter((existing) => existing !== code);
  return on ? [...without, code] : without;
}

/** An instant as a `datetime-local` field shows it, on this browser's clock. */
export function toLocalInput(iso: string): string {
  return localInputOf(new Date(iso));
}

/** What a `datetime-local` field holds, as the instant it means here; empty when it holds nothing valid. */
export function fromLocalInput(local: string): string {
  // `YYYY-MM-DDTHH:mm` without a zone is local time by the ECMAScript spec.
  const instant = new Date(local);
  return Number.isNaN(instant.getTime()) ? '' : instant.toISOString();
}

function localInputOf(instant: Date): string {
  const two = (value: number) => String(value).padStart(2, '0');
  return `${instant.getFullYear()}-${two(instant.getMonth() + 1)}-${two(instant.getDate())}T${two(instant.getHours())}:${two(instant.getMinutes())}`;
}

function trimmed(text: BilingualText): BilingualText {
  return { es: text.es.trim(), en: text.en.trim() };
}
