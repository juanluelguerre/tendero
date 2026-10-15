import type { ProductDeal } from '@tendero/shared-api';
import { formatPrice } from '@tendero/shared-util';

/** A translation key under `card.deal` and what it is filled with. */
export interface DealWords {
  readonly key: string;
  readonly params: Record<string, string | number>;
}

/**
 * What a card says of a promotion it is in (phase 13c), composed here from the
 * terms the server sends as data (ADR 0059) — "5,00 € menos en tu pedido desde
 * 40,00 €" — never a sentence the server wrote. Money is formatted in the reader's
 * culture, like every price on the card. The minimum is the whole order's, as the
 * till reads it.
 */
export function dealWords(deal: ProductDeal, culture: string): DealWords {
  const terms = deal.terms;
  const money = (amount: typeof terms.amountOff) => (amount ? formatPrice(amount, culture) : '');
  const minimum = terms.minimumSubtotal ? money(terms.minimumSubtotal) : null;
  const from = (key: string, params: Record<string, string | number> = {}): DealWords =>
    minimum ? { key: `card.deal.${key}From`, params: { ...params, minimum } } : { key: `card.deal.${key}`, params };

  switch (terms.kind) {
    case 'AmountOffOrder':
      return from('amountOff', { amount: money(terms.amountOff) });
    case 'PercentOffLine':
      return from('percentOff', { percent: new Intl.NumberFormat(culture).format(terms.percent ?? 0) });
    case 'BuyXGetY':
      return { key: 'card.deal.buyXGetY', params: { take: (terms.buy ?? 0) + (terms.free ?? 0), pay: terms.buy ?? 0 } };
    default:
      return from('freeShipping');
  }
}
