import { describe, expect, it } from 'vitest';
import { checkoutProblem } from './checkout-problem';

/**
 * One sentence a shopper can act on, per status code.
 *
 * The UI review of 2026-09-11 paid with the declining card and read "La tarjeta
 * ha sido rechazada: The card was declined by the issuer." — half Spanish, half
 * the provider's English. The provider speaks one language and the shop two,
 * so the shopper gets the shop's sentence and the provider's words go where the
 * shopkeeper reads them: the audit row, which records them verbatim.
 */
const echo = (key: string, params?: Record<string, unknown>) =>
  params ? `${key} ${JSON.stringify(params)}` : key;
const money = (amount: number) => `${amount} €`;

describe('what the checkout tells a shopper', () => {
  it('says the card was declined in the shop language, without the provider sentence', () => {
    const sentence = checkoutProblem(
      { status: 402, error: { detail: 'The card was declined by the issuer.' } },
      echo,
      money,
    );

    expect(sentence).toBe('checkout.declined');
    expect(sentence).not.toContain('issuer');
  });

  it('carries the new total when the price moved', () => {
    expect(checkoutProblem({ status: 409, error: { total: 78.33 } }, echo, money))
      .toBe('checkout.priceChanged {"total":"78.33 €"}');
  });

  it('tells an unreachable provider apart from a decline', () => {
    expect(checkoutProblem({ status: 503 }, echo, money)).toBe('checkout.unavailable');
  });

  it('keeps the server sentence for a request the shopper can correct', () => {
    expect(checkoutProblem({ status: 400, error: "'express' is not offered here" }, echo, money))
      .toBe(`checkout.failed {"reason":"'express' is not offered here"}`);
  });
});
