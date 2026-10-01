import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import type { ProductDetail, VariantView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { of, throwError, type Observable } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CartStore } from '../../cart/cart.store';
import { ProductService } from '../../data-access/product.service';
import { SpecsService } from '../../data-access/specs.service';
import { ProductStore } from './product.store';

const variant = (sku: string, axisValues: Record<string, string>, inStock: boolean, imageId: string | null = null): VariantView =>
  ({ variantId: sku, sku, priceAmount: 29.9, priceCurrency: 'EUR', axisValues, label: sku, imageId, discontinued: false, inStock, remaining: null }) as unknown as VariantView;

/** Navy in 40 (sold out, and listed first) and 38, black only in 38 and with its own photo. */
const aShirt = (): ProductDetail =>
  ({
    code: 'K7M2QX9P4T',
    slug: 'camisa-de-lino',
    name: 'Camisa de lino',
    images: [{ imageId: 'cover' }, { imageId: 'black' }],
    attributes: [],
    axes: [],
    variants: [
      variant('NAVY-40', { COLOR: 'NAVY_BLUE', SIZE: '40' }, false),
      variant('NAVY-38', { COLOR: 'NAVY_BLUE', SIZE: '38' }, true),
      variant('BLACK-38', { COLOR: 'BLACK', SIZE: '38' }, true, 'black'),
    ],
    priceFrom: 29.9,
    priceTo: 29.9,
    priceCurrency: 'EUR',
    alternates: [],
  }) as unknown as ProductDetail;

/** The product page's own state (ADR 0047): the product the URL names, the variant chosen, and what adding it did. */
describe('the product store', () => {
  let answer: () => Observable<ProductDetail>;
  let added: string[];
  const refusal = signal<string | null>(null);

  beforeEach(() => {
    answer = () => of(aShirt());
    added = [];
    refusal.set(null);
    TestBed.configureTestingModule({
      providers: [
        ProductStore,
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ slug: 'camisa-de-lino', code: 'K7M2QX9P4T' })) } },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: ProductService, useValue: { get: () => answer() } },
        { provide: SpecsService, useValue: { get: () => throwError(() => new Error('Knowledge is down')) } },
        { provide: CartStore, useValue: { add: async (sku: string) => void added.push(sku), failed: refusal } },
      ],
    });
  });

  function opened() {
    const store = TestBed.inject(ProductStore);
    TestBed.tick();
    return store;
  }

  it('reads the product the URL names and opens on a variant that can be bought', () => {
    const store = opened();

    expect([store.state().status, store.chosen()?.sku]).toEqual(['done', 'NAVY-38']);
  });

  it('says a code nobody sells is not found, and anything else failed', () => {
    answer = () => throwError(() => ({ status: 404 }));
    const missing = opened().state().status;

    TestBed.resetTestingModule();
    expect(missing).toBe('notFound');
  });

  /**
   * A code that is not a code — a link copied short, an address typed by hand —
   * is refused by the API as malformed (400). For the shopper it is a product
   * that does not exist; "try again in a moment" would never come true.
   */
  it('says a malformed code is not found, not a failure', () => {
    answer = () => throwError(() => ({ status: 400 }));
    const malformed = opened().state().status;

    TestBed.resetTestingModule();
    expect(malformed).toBe('notFound');
  });

  /** The specs are Knowledge's, beside the product: without them the lines fall back to the catalogue's (invariant 8). */
  it('draws the product when Knowledge does not answer', () => {
    const store = opened();

    expect([store.product()?.code, store.statements()]).toEqual(['K7M2QX9P4T', []]);
  });

  it('adds the chosen variant and says so beside the button, until another is chosen', async () => {
    const store = opened();
    await store.addToCart();
    const said = store.notice();
    store.choose('COLOR', 'BLACK');

    expect([added, said, store.notice()]).toEqual([['NAVY-38'], { sku: 'NAVY-38', ok: true }, null]);
  });

  it('says the refusal when the cart keeps one', async () => {
    const store = opened();
    refusal.set('Out of stock');
    await store.addToCart();

    expect(store.notice()?.ok).toBe(false);
  });

  /** A colour has its own photo; choosing it moves the gallery there. */
  it('moves the gallery to the colour chosen', () => {
    const store = opened();
    store.choose('COLOR', 'BLACK');

    expect([store.chosen()?.sku, store.gallery().map((image) => image.imageId)]).toEqual(['BLACK-38', ['black', 'cover']]);
  });
});
