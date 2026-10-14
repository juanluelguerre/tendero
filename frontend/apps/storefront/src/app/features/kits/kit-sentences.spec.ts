import type { KitSlotView, SearchHit } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { budgetBand, kitLines, slotSentence, spentShare } from './kit-sentences';

/**
 * What a kit says of each slot, composed by the page from what the answer
 * carries (decided with the author on the mock-up, 2026-10-13): the API gives
 * the reason, the product and the amounts; the page gives the words and
 * formats the money. The server's own sentences are what an agent reads.
 */
const echo = (key: string, params?: Record<string, unknown>) => (params ? `${key} ${JSON.stringify(params)}` : key);
const money = (amount: { amount: number; currency: string }) => `${amount.amount / 100} €`;

function hit(name: string, amount: number, sku = `${name}-SKU`): SearchHit {
  return { name, matchedSku: sku, price: { amount, currency: 'EUR' } } as SearchHit;
}

function slot(with_: Partial<KitSlotView>): KitSlotView {
  return {
    code: 'KNIFE',
    name: 'Cuchillo',
    required: true,
    requirements: [],
    product: null,
    gap: null,
    better: null,
    ...with_,
  };
}

describe('what a kit says of a slot', () => {
  it('says nothing of a slot that got the best there was', () => {
    expect(slotSentence(slot({ product: hit('Cuchillo de chef', 6400) }), echo, money)).toBeNull();
  });

  it('names what ranked higher and did not fit, and by how much', () => {
    const chosen = slot({
      product: hit('Cuchillo de chef', 6400),
      better: { product: hit('Set de 3 cuchillos', 11900), short: { amount: 4780, currency: 'EUR' } },
    });

    expect(slotSentence(chosen, echo, money)).toBe('kits.said.better {"name":"Set de 3 cuchillos","short":"47.8 €"}');
  });

  it('says what the cheapest costs when nothing fitted what was left', () => {
    const empty = slot({
      gap: { reason: 'OverBudget', cheapest: hit('Tabla de bambú', 2490), short: { amount: 1770, currency: 'EUR' } },
    });

    expect(slotSentence(empty, echo, money)).toBe('kits.said.overBudget {"price":"24.9 €","short":"17.7 €"}');
  });

  it('says so when the shop has nothing for the slot', () => {
    expect(slotSentence(slot({ gap: { reason: 'NoCandidate', cheapest: null, short: null } }), echo, money)).toBe(
      'kits.said.noCandidate',
    );
  });
});

describe('what a kit puts in the basket', () => {
  it('is one of each product chosen, and nothing for an empty slot', () => {
    const slots = [
      slot({ product: hit('Sartén', 4990, 'PAN-26') }),
      slot({ gap: { reason: 'NoCandidate', cheapest: null, short: null } }),
      slot({ product: hit('Cuchillo', 6400, 'KNIFE-20') }),
    ];

    expect(kitLines(slots)).toEqual([
      { sku: 'PAN-26', quantity: 1 },
      { sku: 'KNIFE-20', quantity: 1 },
    ]);
  });
});

describe('the bar under the total', () => {
  it('is the share of the budget spent, never more than all of it', () => {
    expect(spentShare({ amount: 20280, currency: 'EUR' }, { amount: 21000, currency: 'EUR' })).toBeCloseTo(96.57, 1);
    expect(spentShare({ amount: 0, currency: 'EUR' }, { amount: 21000, currency: 'EUR' })).toBe(0);
    expect(spentShare({ amount: 500, currency: 'EUR' }, { amount: 0, currency: 'EUR' })).toBe(0);
  });
});

/** `kit_built` reports the budget in bands: what people mean to spend, never the figure they typed. */
describe('a budget, in the band the event reports', () => {
  it.each([
    [4999, 'under_50'],
    [5000, '50_100'],
    [9999, '50_100'],
    [10000, '100_200'],
    [20000, '200_500'],
    [50000, '500_plus'],
  ])('%i is %s', (amount, band) => {
    expect(budgetBand({ amount, currency: 'EUR' })).toBe(band);
  });
});
