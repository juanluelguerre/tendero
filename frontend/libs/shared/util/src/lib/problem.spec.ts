import { describe, expect, it } from 'vitest';
import { problemOf, refusalSentence } from './problem';

/**
 * A refusal as the API writes every one (phase 13b; RFC 9457): a `code` to
 * branch on and the shop's sentence in the language asked. What a screen does
 * with it is small and the same everywhere, so it is written once.
 */
describe('a refusal from the API', () => {
  const priceChanged = {
    type: 'https://tendero.dev/problems/price-changed',
    title: 'El precio ha cambiado mientras estabas aquí.',
    status: 409,
    code: 'PriceChanged',
    total: { amount: 11390, currency: 'EUR' },
  };

  it('is recognised by its code, and nothing else is one', () => {
    expect(problemOf(priceChanged)?.code).toBe('PriceChanged');
    expect(problemOf('The cart refused that change.')).toBeNull();
    expect(problemOf({ title: 'No code' })).toBeNull();
    expect(problemOf({ code: 'NoTitle' })).toBeNull();
    expect(problemOf(null)).toBeNull();
  });

  it('says its detail when it has one and its title when it does not', () => {
    expect(refusalSentence(priceChanged)).toBe('El precio ha cambiado mientras estabas aquí.');
    expect(refusalSentence({ ...priceChanged, detail: 'Ahora son 113,90 €.' })).toBe('Ahora son 113,90 €.');
  });

  /** A validation problem is the same problem, and what is wrong is in its fields. */
  it('says what is wrong with each field of a request that did not validate', () => {
    const invalid = {
      title: 'La petición no es válida.',
      status: 400,
      code: 'Invalid',
      errors: { quantity: ['Quantity must be positive.'], sku: ['A SKU is needed.', 'Quantity must be positive.'] },
    };

    expect(refusalSentence(invalid)).toBe('Quantity must be positive. A SKU is needed.');
  });

  /**
   * The domain's own sentence — English, written for a log — is shown only to
   * whoever asks for it: the shopkeeper's screens do, the shopper's do not.
   */
  it('adds the domain reason only when asked', () => {
    const refused = {
      title: 'El producto no admite esas variantes.',
      status: 409,
      code: 'VariantsRejected',
      reason: 'Axis COLOR has no option RED.',
    };

    expect(refusalSentence(refused)).toBe('El producto no admite esas variantes.');
    expect(refusalSentence(refused, { withReason: true })).toBe(
      'El producto no admite esas variantes. Axis COLOR has no option RED.',
    );
  });

  it('says nothing of a body that is not a refusal', () => {
    expect(refusalSentence('Internal Server Error')).toBe('');
    expect(refusalSentence(undefined)).toBe('');
  });
});
