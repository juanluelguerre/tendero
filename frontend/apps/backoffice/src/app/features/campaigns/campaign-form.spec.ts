import type { ManagedCampaignView } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import {
  blankForm,
  codeFrom,
  createRequestOf,
  detailsOf,
  differs,
  formOf,
  fromLocalInput,
  toggled,
  toLocalInput,
} from './campaign-form';

describe('the campaign form', () => {
  it('reads a saved campaign, and gives back the request that saves it unchanged', () => {
    const campaign = saved();
    const form = formOf(campaign);

    expect(form.categoryCode).toBeNull();
    expect(detailsOf(form)).toEqual({
      name: campaign.name,
      line: campaign.line,
      kind: 'Event',
      categoryCode: null,
      startsAt: campaign.startsAt,
      endsAt: campaign.endsAt,
      priority: 100,
      promotionCodes: ['SUMMER25', 'SHIP0'],
      image: { imageId: 'abc123', alt: { es: 'Un toldo', en: 'An awning' } },
    });
    expect(differs(form, formOf(campaign))).toBe(false);
  });

  it('keeps a category scope, and sends the whole shop as no category', () => {
    expect(formOf(saved({ scope: 'Category', categoryCode: 'HOME' })).categoryCode).toBe('HOME');
    expect(detailsOf({ ...formOf(saved()), categoryCode: '' as unknown as null }).categoryCode).toBeNull();
  });

  it('trims the words it sends, and sends no image when there is none', () => {
    const form = { ...formOf(saved()), name: { es: ' Tendero Days ', en: 'Tendero Days  ' }, image: null };

    expect(detailsOf(form).name).toEqual({ es: 'Tendero Days', en: 'Tendero Days' });
    expect(detailsOf(form).image).toBeNull();
  });

  it('creates with the code it was given, and says a changed field is a change', () => {
    const form = { ...blankForm(new Date(2026, 8, 29, 10, 0)), code: ' TENDERO_DAYS ' };

    expect(createRequestOf(form).code).toBe('TENDERO_DAYS');
    expect(differs({ ...form, priority: 11 }, form)).toBe(true);
    expect(differs({ ...form, promotionCodes: ['SHIP0'] }, form)).toBe(true);
  });

  it('starts a new campaign as an event opening tomorrow at midnight, a week long', () => {
    const form = blankForm(new Date(2026, 8, 29, 10, 30));

    expect(form.kind).toBe('Event');
    expect(form.startsAt).toBe('2026-09-30T00:00');
    expect(form.endsAt).toBe('2026-10-06T23:59');
    expect(form.promotionCodes).toEqual([]);
  });
});

describe('a date on the form', () => {
  it('is shown on this browser’s clock and sent back as the same instant', () => {
    const instant = '2026-10-01T00:00:00.000Z';

    expect(fromLocalInput(toLocalInput(instant))).toBe(instant);
  });

  it('is the local time the field says', () => {
    expect(fromLocalInput('2026-10-07T23:59')).toBe(new Date(2026, 9, 7, 23, 59).toISOString());
    expect(toLocalInput(new Date(2026, 9, 7, 23, 59).toISOString())).toBe('2026-10-07T23:59');
  });

  it('is nothing when the field holds nothing', () => {
    expect(fromLocalInput('')).toBe('');
  });
});

describe('a code from a name', () => {
  it.each([
    ['Tendero Days', 'TENDERO_DAYS'],
    ['Semana del hogar', 'SEMANA_DEL_HOGAR'],
    ['Otoño en Tendero', 'OTONO_EN_TENDERO'],
    ['  ¡Black   Friday! ', 'BLACK_FRIDAY'],
    ['2x1 en menaje', 'C_2X1_EN_MENAJE'],
    ['', ''],
  ])('%s is %s', (name, code) => {
    expect(codeFrom(name)).toBe(code);
  });

  it('is never longer than the server allows', () => {
    expect(codeFrom('a very long campaign name that goes on and on and on').length).toBeLessThanOrEqual(40);
    expect(codeFrom('a very long campaign name that goes on and on and on')).not.toMatch(/_$/);
  });
});

describe('a promotion checked or unchecked', () => {
  it('is added once and removed', () => {
    expect(toggled(['SUMMER25'], 'SHIP0', true)).toEqual(['SUMMER25', 'SHIP0']);
    expect(toggled(['SUMMER25', 'SHIP0'], 'SHIP0', true)).toEqual(['SUMMER25', 'SHIP0']);
    expect(toggled(['SUMMER25', 'SHIP0'], 'SUMMER25', false)).toEqual(['SHIP0']);
  });
});

function saved(overrides: Partial<ManagedCampaignView> = {}): ManagedCampaignView {
  return {
    code: 'TENDERO_DAYS',
    name: { es: 'Tendero Days', en: 'Tendero Days' },
    line: { es: 'Hasta −25 %', en: 'Up to 25% off' },
    kind: 'Event',
    scope: 'Shop',
    categoryCode: null,
    startsAt: '2026-10-01T00:00:00.000Z',
    endsAt: '2026-10-07T23:59:00.000Z',
    priority: 100,
    promotionCodes: ['SUMMER25', 'SHIP0'],
    image: { imageId: 'abc123', alt: { es: 'Un toldo', en: 'An awning' } },
    status: 'Draft',
    phase: 'Upcoming',
    endedAt: null,
    createdAt: '2026-09-28T10:00:00Z',
    updatedAt: '2026-09-28T10:00:00Z',
    ...overrides,
  };
}
