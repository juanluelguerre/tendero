import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ProductForReview } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { ProductReview } from './product-review';

const Products = 'http://api/api/catalog/products';

/** A pack in black and grey, with a cover that says nothing and a side view that says black. */
const aPack = (over: Partial<ProductForReview> = {}): ProductForReview => ({
  productId: 'p1',
  code: 'MOCHILA',
  status: 'Draft',
  names: { es: 'Mochila', en: 'Backpack' },
  descriptions: {},
  brand: null,
  category: 'Bolsas',
  price: { amount: 4900, currency: 'EUR' },
  imageIds: ['cover', 'side'],
  attributes: [],
  missingCultures: [],
  updatedAt: '2026-10-08T06:00:00Z',
  images: [
    { id: 'cover', alt: {}, shows: {} },
    { id: 'side', alt: {}, shows: { COLOR: 'BLACK' } },
  ],
  axes: [
    {
      code: 'COLOR',
      label: 'color',
      options: [
        { code: 'BLACK', label: 'negro' },
        { code: 'GREY', label: 'gris' },
      ],
    },
  ],
  missingRequired: [],
  ...over,
});

/**
 * A product as a person reviews it, on the two things the catalogue's model
 * gained in phase 13a: what its category requires and it lacks (ADR 0056,
 * amended), and which colour each of its pictures shows (ADR 0057).
 */
describe('the product review', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function review(product: ProductForReview) {
    const fixture = TestBed.createComponent(ProductReview);
    fixture.componentRef.setInput('code', product.code);
    fixture.detectChanges();
    TestBed.tick();
    http.match((req) => req.url.endsWith('/review')).forEach((req) => req.flush(product));
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await TestBed.inject(ApplicationRef).whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('names what the product lacks of what its category requires', async () => {
    const page = await review(
      aPack({
        missingRequired: [
          { code: 'MATERIAL', label: 'material' },
          { code: 'CAPACITY_L', label: 'capacidad' },
        ],
      }),
    );

    const lacking = page.querySelector('[data-test="missing-required"]');
    expect([...(lacking?.querySelectorAll('li') ?? [])].map((item) => item.textContent?.trim())).toEqual([
      'material',
      'capacidad',
    ]);
  });

  it('says nothing of it for a product that lacks nothing', async () => {
    const page = await review(aPack());

    expect(page.querySelector('[data-test="missing-required"]')).toBeNull();
  });

  /** One control an axis beside each picture: what a variant still sold holds, and none in particular — on what is stored. */
  it('offers each picture the colours on sale, on the one it says it shows', async () => {
    const page = await review(aPack());

    const controls = [...page.querySelectorAll('select')];
    expect(controls.map((select) => [...select.options].map((option) => option.value))).toEqual([
      ['', 'BLACK', 'GREY'],
      ['', 'BLACK', 'GREY'],
    ]);
    expect(controls.map((select) => select.value)).toEqual(['', 'BLACK']);
  });

  it('writes the colour chosen for a picture, and nothing at all for none in particular', async () => {
    const page = await review(aPack());
    const [cover, side] = [...page.querySelectorAll('select')];

    cover.value = 'GREY';
    cover.dispatchEvent(new Event('change'));
    side.value = '';
    side.dispatchEvent(new Event('change'));

    expect([
      http.expectOne(`${Products}/p1/images/cover/shows`).request.body,
      http.expectOne(`${Products}/p1/images/side/shows`).request.body,
    ]).toEqual([{ shows: { COLOR: 'GREY' } }, { shows: {} }]);
  });

  it('has no such control for a product bought one way', async () => {
    const page = await review(aPack({ axes: [] }));

    expect(page.querySelectorAll('select').length).toBe(0);
  });
});
