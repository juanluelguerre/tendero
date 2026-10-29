import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { OrdersExport } from './orders-export';

/** Orders for the accountant (phase 16, E-4): folded until asked for, then a span of days and a download. */
describe('the orders export', () => {
  beforeEach(() => TestBed.resetTestingModule());

  async function open() {
    TestBed.configureTestingModule({
      imports: [OrdersExport],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
    const fixture = TestBed.createComponent(OrdersExport);
    fixture.detectChanges();
    // `*transloco` renders nothing until its language arrives: answered empty.
    for (const translations of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/')))
      translations.flush({});
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  function find<T extends Element>(root: HTMLElement, selector: string): T {
    const found = root.querySelector<T>(selector);
    if (!found) throw new Error(`Nothing matches ${selector}.`);
    return found;
  }

  it('stays folded until it is asked for', async () => {
    const element = (await open()).nativeElement as HTMLElement;

    expect(element.querySelector('input[type="date"]')).toBeNull();
    expect(find(element, 'button[aria-expanded]').getAttribute('aria-expanded')).toBe('false');
  });

  it('asks for the span the dates say', async () => {
    const fixture = await open();
    const element = fixture.nativeElement as HTMLElement;
    find<HTMLButtonElement>(element, 'button[aria-expanded]').click();
    fixture.detectChanges();

    const [from, to] = [...element.querySelectorAll<HTMLInputElement>('input[type="date"]')];
    from.value = '2026-10-01';
    from.dispatchEvent(new Event('input'));
    to.value = '2026-10-31';
    to.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    find<HTMLButtonElement>(element, 'button[data-download]').click();

    const request = TestBed.inject(HttpTestingController).expectOne(
      (req) => req.url === 'http://api/api/exports/orders',
    );
    expect([request.request.params.get('from'), request.request.params.get('to')]).toEqual([
      '2026-10-01',
      '2026-10-31',
    ]);
  });
});
