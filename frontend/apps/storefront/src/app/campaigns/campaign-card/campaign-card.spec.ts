import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideOAuthClient } from 'angular-oauth2-oidc';
import type { CampaignView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { CampaignCard } from './campaign-card';

/**
 * The campaign card's states (R-8, H-2; the approved mock-ups): an event running
 * and before it starts, a season, with an image of its own, and the banner.
 */
describe('the campaign card', () => {
  let events: [string, unknown][];

  beforeEach(() => {
    events = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideOAuthClient(),
        provideTenderoI18n(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: 'http://api' },
        {
          provide: Telemetry,
          useValue: { trackEvent: (n: string, a: unknown) => events.push([n, a]) } as unknown as Telemetry,
        },
      ],
    });
  });

  async function render(campaign: CampaignView, variant: 'lead' | 'banner' = 'lead') {
    const fixture = TestBed.createComponent(CampaignCard);
    fixture.componentRef.setInput('campaign', campaign);
    fixture.componentRef.setInput('variant', variant);
    fixture.detectChanges();
    for (const request of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/')))
      request.flush(TRANSLATIONS);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('counts down to the end of a running event, and says so once to a screen reader', async () => {
    const { element } = await render(event({ phase: 'Running' }));

    expect(element.classList).toContain('campaign--event');
    expect(element.querySelector('storefront-countdown-clock')?.textContent).toContain('Termina en');
    // The spoken sentence is an ordinary element, never a live region.
    expect(element.querySelector('[aria-live]')).toBeNull();
    expect(element.querySelector('.campaign__cta')?.textContent).toContain('Ver las ofertas');
    expect(element.querySelector('.campaign__cta')?.getAttribute('href')).toContain('campaign=TENDERO_DAYS');
  });

  it('counts down to the start before it opens, and invites to see what it will hold', async () => {
    const { element } = await render(event({ phase: 'Upcoming' }));

    expect(element.querySelector('storefront-countdown-clock')?.textContent).toContain('Empieza en');
    expect(element.querySelector('.campaign__cta')?.textContent).toContain('Ver qué incluirá');
  });

  it('draws a season light, with no clock', async () => {
    const { element } = await render(event({ kind: 'Season', name: 'Otoño en Tendero' }));

    expect(element.classList).toContain('campaign--season');
    expect(element.querySelector('storefront-countdown-clock')).toBeNull();
    expect(element.querySelector('.campaign__when')?.textContent).toContain('Hasta el');
  });

  it('names a coupon promotion with its code', async () => {
    const { element } = await render(
      event({
        holds: [
          { code: 'BLACKFRIDAY', name: 'Black Friday', couponCode: 'BF2026' },
          { code: 'SHIRT3X2', name: '3x2 en camisetas', couponCode: null },
        ],
      }),
    );

    expect([...element.querySelectorAll('.campaign__hold')].map((li) => li.textContent?.trim())).toEqual([
      'Black Friday con el código BF2026',
      '3x2 en camisetas',
    ]);
  });

  it('shows its own image with its alt text, and no products beside it', async () => {
    const { element } = await render(
      event({ imageId: 'campaigns/tendero-days.webp', imageAlt: 'Una tienda con toldo' }),
    );

    const image = element.querySelector('.campaign__image') as HTMLImageElement;
    expect(image.getAttribute('src')).toBe('http://api/api/images/campaigns%2Ftendero-days.webp');
    expect(image.alt).toBe('Una tienda con toldo');
    expect(element.querySelector('.campaign__products')).toBeNull();
  });

  it('as a banner, has no picture and no clay button, only a link', async () => {
    const { element } = await render(event({ imageId: 'x' }), 'banner');

    expect(element.querySelector('.campaign__image')).toBeNull();
    expect(element.querySelector('.button')).toBeNull();
    expect(element.querySelector('.campaign__link')).not.toBeNull();
  });

  it('reports being opened, with the phase in the plan words', async () => {
    const { element } = await render(event({ phase: 'Upcoming' }));

    // With Ctrl held the router leaves the page alone (a new tab's gesture), and the
    // browser's own default is cancelled at the document: the click still counts.
    element.ownerDocument.addEventListener('click', (click) => click.preventDefault(), { once: true });
    element
      .querySelector('.campaign__cta')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));

    expect(events).toEqual([['campaign_card_opened', { campaign: 'TENDERO_DAYS', phase: 'before' }]]);
  });
});

const TRANSLATIONS = {
  campaign: {
    endsIn: 'Termina en',
    startsIn: 'Empieza en',
    until: 'Hasta el {{date}}',
    from: 'Desde el {{date}}',
    holdWithCode: '{{name}} con el código {{code}}',
    seeOffers: 'Ver las ofertas',
    seeUpcoming: 'Ver qué incluirá',
    seeSeason: 'Ver la temporada',
  },
};

function event(overrides: Partial<CampaignView>): CampaignView {
  return {
    code: 'TENDERO_DAYS',
    name: 'Tendero Days',
    line: 'Una semana de precios de tienda de barrio',
    kind: 'Event',
    scope: 'Shop',
    categoryCode: null,
    phase: 'Running',
    startsAt: '2099-10-01T00:00:00+02:00',
    endsAt: '2099-10-07T23:59:00+02:00',
    priority: 100,
    holds: [],
    imageId: null,
    imageAlt: null,
    ...overrides,
  };
}
