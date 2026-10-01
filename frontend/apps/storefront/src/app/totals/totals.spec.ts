import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { beforeEach, describe, expect, it } from 'vitest';
import { type ShippingState, Totals } from './totals';

@Component({
  imports: [Totals],
  template: `<storefront-totals
    currency="EUR"
    [subtotal]="100"
    [discountTotal]="discount()"
    [shipping]="4.95"
    [shippingState]="state()"
    [shippingLabel]="label()"
    [total]="99.95"
    [taxTotal]="tax()"
  />`,
})
class Host {
  readonly discount = signal(0);
  readonly state = signal<ShippingState>('charged');
  readonly label = signal<string | null>(null);
  readonly tax = signal(17.35);
}

const words = {
  cart: {
    subtotal: 'Subtotal',
    discounts: 'Discounts',
    shipping: 'Delivery',
    shippingAtCheckout: 'Worked out at checkout',
    total: 'Total',
    tax: 'VAT included',
  },
};

/**
 * The figures an order is summed up with, in the one shape the cart, the
 * checkout and the order share (ADR 0049): the server's numbers, formatted and
 * never added up here.
 */
describe('the totals', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: words },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [{ provide: CultureStore, useValue: { active: () => 'en' } }],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const rows = (fixture: ReturnType<typeof render>) =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('dl > div')].map((row) => [
      row.querySelector('dt')?.textContent?.trim(),
      row.querySelector('dd')?.textContent?.trim(),
    ]);
  const text = (fixture: ReturnType<typeof render>) => (fixture.nativeElement as HTMLElement).textContent ?? '';

  it('lists subtotal, delivery and total, and the tax the total includes', () => {
    const fixture = render();
    expect(rows(fixture).map(([label]) => label)).toEqual(['Subtotal', 'Delivery', 'Total']);
    expect(rows(fixture)[2][1]).toContain('99.95');
    expect(text(fixture)).toContain('VAT included');
    expect(text(fixture)).toContain('17.35');
  });

  it('shows the discounts only when there are some, as money off', () => {
    const fixture = render();
    fixture.componentInstance.discount.set(5);
    fixture.detectChanges();
    expect(rows(fixture)[1][0]).toBe('Discounts');
    expect(rows(fixture)[1][1]).toMatch(/^−.*5\.00/);
  });

  it('leaves the tax out when there is none', () => {
    const fixture = render();
    fixture.componentInstance.tax.set(0);
    fixture.detectChanges();
    expect(text(fixture)).not.toContain('VAT included');
  });

  it('says the delivery is worked out later until a rate is charged', () => {
    const fixture = render();
    expect(rows(fixture)[1][1]).toContain('4.95');

    for (const state of ['at-checkout', 'to-choose'] as const) {
      fixture.componentInstance.state.set(state);
      fixture.detectChanges();
      expect(rows(fixture)[1][1]).toBe('Worked out at checkout');
    }
  });

  it('names the delivery by its rate once there is one', () => {
    const fixture = render();
    fixture.componentInstance.label.set('Standard delivery');
    fixture.detectChanges();
    expect(rows(fixture)[1][0]).toBe('Standard delivery');
  });
});
