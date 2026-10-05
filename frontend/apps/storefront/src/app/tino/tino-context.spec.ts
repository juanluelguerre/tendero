import { describe, expect, it } from 'vitest';
import { contextOf, pageOf } from './tino-context';

const Order = '0199c2f4-7d3e-7a11-9b55-3f1f0c2a9e10';

/** What the shopper is looking at, read from the address: only where "it" means one thing. */
describe("tino's context", () => {
  it("is the product of a product's page, by its code and never its slug", () => {
    expect(contextOf('/es/p/sarten-de-hierro-fundido/4YGHPDS6Y0')).toEqual({ kind: 'Product', key: '4YGHPDS6Y0' });
    expect(contextOf('/en/p/cast-iron-pan/4YGHPDS6Y0?via=search#specs')).toEqual({
      kind: 'Product',
      key: '4YGHPDS6Y0',
    });
  });

  /** A guest's order is read with its id: it travels beside the question, as the page's address already holds it. */
  it("is the order of an order's page, by its id", () => {
    expect(contextOf(`/es/orders/${Order}`)).toEqual({ kind: 'Order', key: Order });
    expect(contextOf(`/en/orders/${Order}?placed=1`)).toEqual({ kind: 'Order', key: Order });
  });

  it('is nothing where there is no one thing on the screen, or what is there is not an id', () => {
    for (const url of [
      '/es',
      '/es?q=sarten',
      '/es/c/KITCHEN',
      '/es/cart',
      '/es/orders/12345',
      '/es/orders/not-an-id',
      '/es/account',
    ])
      expect(contextOf(url), url).toBeNull();
  });

  it('names the kind of page for the event that says where the door is used from', () => {
    expect(pageOf('/es')).toBe('home');
    expect(pageOf('/es/')).toBe('home');
    expect(pageOf('/es?q=sarten')).toBe('search');
    expect(pageOf('/es?offer=1')).toBe('home');
    expect(pageOf('/es/c/KITCHEN?brand=Vertex')).toBe('category');
    expect(pageOf('/en/p/cast-iron-pan/4YGHPDS6Y0')).toBe('product');
    expect(pageOf(`/es/orders/${Order}`)).toBe('order');
    expect(pageOf('/es/cart')).toBe('cart');
    expect(pageOf('/es/checkout')).toBe('other');
    expect(pageOf('/es/account')).toBe('other');
  });
});
