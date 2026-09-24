import { scrubUrl } from './scrub-url';

describe('scrubUrl', () => {
  it('drops what the shopper typed: the query string and the fragment', () => {
    expect(scrubUrl('http://localhost:4200/api/search?q=regalo%20para%20Ana&culture=es#top')).toBe(
      'http://localhost:4200/api/search',
    );
  });

  it('replaces an order id with :id, so one order is not one URL', () => {
    expect(scrubUrl('/api/orders/3f2c1d4e-5b6a-4c7d-8e9f-0a1b2c3d4e5f/returns')).toBe('/api/orders/:id/returns');
    expect(scrubUrl('/api/orders/3f2c1d4e5b6a4c7d8e9f0a1b2c3d4e5f')).toBe('/api/orders/:id');
  });

  it('replaces long key material, and keeps what describes the shop', () => {
    expect(scrubUrl('/api/images/a1b2c3d4e5f6a7b8c9d0e1f2')).toBe('/api/images/:id');
    expect(scrubUrl('/api/catalog/products/B13BAG0710')).toBe('/api/catalog/products/B13BAG0710');
    expect(scrubUrl('/es/p/B13BAG0710/mochila-urbana-azul-marino')).toBe('/es/p/B13BAG0710/mochila-urbana-azul-marino');
  });
});
