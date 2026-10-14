import type { SearchHit, TinoAnswer, TinoReason } from '@tendero/shared-api';

/** What the specs of Tino's store and panel are answered with: a sentence, a card, an answer, a turn that did not answer. */
export const reason = (
  text: string,
  source: TinoReason['source'] = 'DataSheet',
  fact = 'material: hierro',
): TinoReason => ({
  text,
  source,
  fact,
  quotes: source === 'Claim' ? ['base de acero para inducción'] : [],
  support: source === 'Claim' ? 0.97 : null,
});

export const hit = (code: string, name = 'Sartén de hierro'): SearchHit =>
  ({
    productId: `id-${code}`,
    code,
    name,
    slug: 'sarten',
    brand: 'Vertex',
    category: 'Sartenes',
    matchedVariantId: `v-${code}`,
    matchedSku: `SKU-${code}`,
    price: { amount: 4200, currency: 'EUR' },
    priceFrom: { amount: 4200, currency: 'EUR' },
    priceTo: { amount: 4200, currency: 'EUR' },
    imageId: null,
    inStock: true,
    score: 1,
  }) as SearchHit;

export const answer = (extra: Partial<TinoAnswer> = {}): TinoAnswer => ({
  status: 'Answered',
  cause: null,
  kind: 'Products',
  products: [
    { code: 'PAN-1', name: 'Sartén de hierro', reasons: [reason('Vale para inducción.', 'Claim', 'inducción: sí')] },
  ],
  says: [],
  ask: null,
  noData: null,
  reads: [{ tool: 'search_products', arguments: { text: 'sartén inducción', culture: 'es' }, rows: 2, total: 14 }],
  searchText: 'una sartén para inducción',
  reasonsDropped: 1,
  cost: { calls: 4, inputTokens: 3000, outputTokens: 200, microEuros: 0, milliseconds: 4100, model: 'qwen' },
  profile: 'local',
  fellBack: false,
  conversationId: 'conv-1',
  order: null,
  ...extra,
});

export const unavailable = (cause: NonNullable<TinoAnswer['cause']>): TinoAnswer =>
  answer({
    status: 'Unavailable',
    cause,
    kind: null,
    products: [],
    reads: [],
    cost: null,
    profile: null,
    reasonsDropped: 0,
  });
