import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SidePaneSlotForSpecs } from '../../shell/side-pane/side-pane-slot.testing';
import { CampaignsPage } from './campaigns-page';

const Calendar = 'http://api/api/pricing/campaign-calendar';

/**
 * The Campaigns screen (R-8, H-1b) as ADR 0042's list and pane: the chips and
 * their counts from status and phase together, a new campaign's request, and a
 * scheduled one that reads without being edited.
 */
describe('the campaigns page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'campaigns', component: CampaignsPage }]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function open(url: string, campaigns: ManagedCampaignView[]) {
    const harness = await RouterTestingHarness.create(url);
    http.expectOne(Calendar).flush(campaigns);
    http.expectOne('http://api/api/pricing/promotions').flush({ items: [promotion('SUMMER25'), promotion('SHIP0')] });
    http.expectOne((req) => req.url === 'http://api/api/catalog/categories').flush({ items: [] });
    // `*transloco` renders nothing until its language arrives; the words are not what is asserted.
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    const slot = TestBed.createComponent(SidePaneSlotForSpecs);
    slot.autoDetectChanges();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    slot.detectChanges();
    // The page and the shell's slot, as a person sees them together.
    const together = document.body.appendChild(document.createElement('div'));
    together.append(harness.routeNativeElement as HTMLElement, slot.nativeElement as HTMLElement);
    return together;
  }

  const counts = (page: HTMLElement) =>
    [...page.querySelectorAll('backoffice-filter-chips .chip__count')].map((count) => Number(count.textContent));

  it('counts every chip from status and phase together, and opens on what is running', async () => {
    const page = await open('/campaigns', [
      campaign('TENDERO_DAYS', 'Scheduled', 'Running'),
      campaign('BLACK_FRIDAY', 'Scheduled', 'Upcoming'),
      campaign('NAVIDAD', 'Scheduled', 'Upcoming'),
      campaign('HOGAR', 'Draft', 'Upcoming'),
      campaign('VERANO', 'Ended', 'Over'),
      campaign('OLVIDADA', 'Draft', 'Over'),
    ]);

    expect(counts(page)).toEqual([1, 2, 1, 2, 6]);
    expect([...page.querySelectorAll('tbody .row__open')].map((row) => row.textContent?.trim())).toEqual([
      'TENDERO_DAYS',
    ]);
  });

  it('shows the drafts when that is the filter in the URL', async () => {
    const page = await open('/campaigns?status=drafts', [
      campaign('HOGAR', 'Draft', 'Upcoming'),
      campaign('TENDERO_DAYS', 'Scheduled', 'Running'),
    ]);

    expect([...page.querySelectorAll('tbody .row__open')].map((row) => row.textContent?.trim())).toEqual(['HOGAR']);
  });

  it('creates a new campaign with the code its name gives it', async () => {
    const page = await open('/campaigns?campaign=new', []);
    const pane = page.querySelector('backoffice-worklist-pane') as HTMLElement;
    expect(pane).not.toBeNull();

    const pairs = pane.querySelectorAll<HTMLFieldSetElement>('fieldset.pair');
    type(pairs[0].querySelectorAll('input')[0], 'Semana del hogar');
    type(pairs[0].querySelectorAll('input')[1], 'Home week');
    type(pairs[1].querySelectorAll('input')[0], 'Todo para la casa');
    type(pairs[1].querySelectorAll('input')[1], 'All for the home');
    const box = pane.querySelector('input[type=checkbox]') as HTMLInputElement;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    TestBed.tick();

    (pane.querySelector('form.editor button[type=submit]') as HTMLButtonElement).click();

    const request = http.expectOne((req) => req.url === Calendar && req.method === 'POST');
    expect(request.request.body).toMatchObject({
      code: 'HOME_WEEK',
      details: {
        name: { es: 'Semana del hogar', en: 'Home week' },
        line: { es: 'Todo para la casa', en: 'All for the home' },
        kind: 'Event',
        categoryCode: null,
        promotionCodes: ['SUMMER25'],
        image: null,
      },
    });
  });

  it('opens a scheduled campaign to read, with the way back to a draft and no Save', async () => {
    const page = await open('/campaigns?campaign=TENDERO_DAYS', [campaign('TENDERO_DAYS', 'Scheduled', 'Running')]);
    const pane = page.querySelector('backoffice-worklist-pane') as HTMLElement;

    expect((pane.querySelector('fieldset.fields') as HTMLFieldSetElement).disabled).toBe(true);
    expect(pane.querySelector('form.editor button[type=submit]')).toBeNull();
    expect([...pane.querySelectorAll('button')].map((button) => button.textContent?.trim())).toContain(
      'campaigns.unschedule',
    );
  });

  it('shows the server’s refusal in the pane', async () => {
    const page = await open('/campaigns?status=drafts&campaign=HOGAR', [campaign('HOGAR', 'Draft', 'Upcoming')]);
    const pane = page.querySelector('backoffice-worklist-pane') as HTMLElement;

    [...pane.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'campaigns.schedule')?.click();
    http
      .expectOne(`${Calendar}/HOGAR/schedule`)
      .flush('The window closes at 2026-01-01, which has already passed.', { status: 400, statusText: 'Bad Request' });
    TestBed.tick();

    expect(pane.querySelector('[role=alert]')?.textContent?.trim()).toBe(
      'The window closes at 2026-01-01, which has already passed.',
    );
  });
});

function type(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
  TestBed.tick();
}

function campaign(
  code: string,
  status: ManagedCampaignView['status'],
  phase: ManagedCampaignView['phase'],
): ManagedCampaignView {
  return {
    code,
    name: { es: code, en: code },
    line: { es: 'Lema', en: 'Line' },
    kind: 'Event',
    scope: 'Shop',
    categoryCode: null,
    startsAt: '2026-10-01T00:00:00Z',
    endsAt: '2026-10-07T23:59:00Z',
    priority: 10,
    promotionCodes: ['SUMMER25'],
    image: null,
    status,
    phase,
    endedAt: null,
    createdAt: '2026-09-28T10:00:00Z',
    updatedAt: '2026-09-28T10:00:00Z',
  };
}

function promotion(code: string) {
  return {
    code,
    name: { es: code, en: code },
    effect: 'percent-off-line',
    effectDetail: '25 %',
    combination: 'Stackable',
    priority: 10,
    exclusivityGroup: null,
    segment: null,
    couponCode: null,
    validFrom: null,
    validTo: null,
    isActive: true,
    missingCultures: [],
  };
}
