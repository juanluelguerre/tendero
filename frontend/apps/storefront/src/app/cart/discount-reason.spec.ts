import type { AppliedDiscount } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { discountReason } from './discount-reason';

/**
 * Why a promotion did not apply, said by the page from the code and the data
 * beside it (ADR 0059).
 *
 * Found in a browser on 2026-10-13: the basket said *un mínimo de 40.00 EUR y
 * artículos de KITCHEN* — the server's sentence, with money written a
 * machine's way and a category by its code. A page formats money for its
 * reader and has the category's name; the server's sentence stays what an
 * agent reads.
 */
const echo = (key: string, params?: Record<string, unknown>) => (params ? `${key} ${JSON.stringify(params)}` : key);

const words = {
  t: echo,
  money: (amount: { amount: number; currency: string }) => `${amount.amount / 100} €`,
  category: (code: string) => (code === 'KITCHEN' ? 'Cocina' : null),
  product: (sku: string) => (sku === 'PAN-26' ? 'Sartén de 26 cm' : null),
};

function suppressed(reason: Partial<AppliedDiscount>): AppliedDiscount {
  return {
    promotionCode: 'P',
    label: 'P',
    amount: { amount: 0, currency: 'EUR' },
    effect: 'percent-off-line',
    combination: 'Stackable',
    outcome: 'Suppressed',
    reasonCode: null,
    reason: null,
    requires: null,
    winner: null,
    ...reason,
  } as AppliedDiscount;
}

const nothing = {
  minimumSubtotal: null,
  minimumQuantity: null,
  categoryCode: null,
  sku: null,
  shortBy: null,
  unitsShort: null,
};

describe('why a promotion did not apply', () => {
  it('says a minimum as money and a category by its name, and how much is missing', () => {
    const sentence = discountReason(
      suppressed({
        reasonCode: 'ConditionNotMet',
        reason: 'No se cumple la condición: un mínimo de 40.00 EUR y artículos de KITCHEN.',
        requires: {
          ...nothing,
          minimumSubtotal: { amount: 4000, currency: 'EUR' },
          categoryCode: 'KITCHEN',
          shortBy: { amount: 1, currency: 'EUR' },
        },
      }),
      words,
    );

    expect(sentence).toBe(
      'cart.reason.conditionNotMet {"what":"cart.reason.minimum {\\"amount\\":\\"40 €\\"}cart.reason.and' +
        'cart.reason.category {\\"name\\":\\"Cocina\\"}"} cart.reason.shortBy {"amount":"0.01 €"}',
    );
    expect(sentence).not.toContain('KITCHEN');
    expect(sentence).not.toContain('EUR');
  });

  it('counts the units missing, one or several', () => {
    const three = { ...nothing, minimumQuantity: 3, categoryCode: 'SHIRT' };

    expect(
      discountReason(suppressed({ reasonCode: 'ConditionNotMet', requires: { ...three, unitsShort: 1 } }), words),
    ).toContain('cart.reason.oneUnitShort');
    expect(
      discountReason(suppressed({ reasonCode: 'ConditionNotMet', requires: { ...three, unitsShort: 2 } }), words),
    ).toContain('cart.reason.unitsShort {"count":2}');
  });

  it('names an article by what the basket calls it, and a category it has no name for by its code', () => {
    expect(
      discountReason(suppressed({ reasonCode: 'ConditionNotMet', requires: { ...nothing, sku: 'PAN-26' } }), words),
    ).toContain('cart.reason.article {\\"name\\":\\"Sartén de 26 cm\\"}');

    expect(
      discountReason(
        suppressed({ reasonCode: 'ConditionNotMet', requires: { ...nothing, categoryCode: 'GARDEN' } }),
        words,
      ),
    ).toContain('cart.reason.category {\\"name\\":\\"GARDEN\\"}');
  });

  it('names the promotion that won', () => {
    expect(discountReason(suppressed({ reasonCode: 'NotCombinableWith', winner: 'Rebajas de verano' }), words)).toBe(
      'cart.reason.notCombinableWith {"winner":"Rebajas de verano"}',
    );
    expect(discountReason(suppressed({ reasonCode: 'SupersededByExclusive', winner: 'Black Friday' }), words)).toBe(
      'cart.reason.supersededByExclusive {"winner":"Black Friday"}',
    );
    expect(discountReason(suppressed({ reasonCode: 'NothingToDiscount' }), words)).toBe(
      'cart.reason.nothingToDiscount',
    );
  });

  it("falls back on the shop's own sentence for a reason it has no words for", () => {
    expect(
      discountReason(suppressed({ reasonCode: 'SomethingNew' as never, reason: 'La frase del servidor.' }), words),
    ).toBe('La frase del servidor.');
    expect(discountReason(suppressed({ outcome: 'Applied' }), words)).toBeNull();
  });
});
