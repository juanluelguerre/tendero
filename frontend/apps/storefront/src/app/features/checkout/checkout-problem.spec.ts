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
const echo = (key: string, params?: Record<string, unknown>) => (params ? `${key} ${JSON.stringify(params)}` : key);
const money = (amount: { amount: number; currency: string }) => `${amount.amount} ${amount.currency}`;

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
    expect(
      checkoutProblem(
        { status: 409, error: { code: 'PriceChanged', total: { amount: 7833, currency: 'EUR' } } },
        echo,
        money,
      ),
    ).toBe('checkout.priceChanged {"total":"7833 EUR"}');
  });

  /**
   * A 409 is several refusals, told apart by their code. Every one of them
   * used to read *the price changed* — and the shelf being short is the one a
   * shopper most needs told as what it is: nothing was charged, and how many
   * there are.
   */
  it('says the shelf is short, with how many there are of each', () => {
    const short = {
      code: 'InsufficientStock',
      shortages: [
        { sku: 'PAN-26', requested: 5, available: 3 },
        { sku: 'KETTLE', requested: 2, available: 1 },
        { sku: 'LAMP', requested: 1, available: 0 },
      ],
    };

    // Three, one and none are three sentences: *solo quedan 1* is not Spanish.
    expect(
      checkoutProblem({ status: 409, error: short }, echo, money, (sku) => (sku === 'PAN-26' ? 'Sartén' : null)),
    ).toBe(
      'checkout.insufficientStock {"what":"checkout.shortOf {\\"name\\":\\"Sartén\\",\\"available\\":3}' +
        'checkout.shortAndcheckout.oneOf {\\"name\\":\\"KETTLE\\"}' +
        'checkout.shortAndcheckout.noneOf {\\"name\\":\\"LAMP\\"}"}',
    );
  });

  it('does not call another conflict a price change', () => {
    const reused = { code: 'IdempotencyKeyReused', title: 'Esa clave ya se usó con otra petición.' };

    expect(checkoutProblem({ status: 409, error: reused }, echo, money)).toBe(
      'checkout.failed {"reason":"Esa clave ya se usó con otra petición."}',
    );
  });

  it('tells an unreachable provider apart from a decline', () => {
    expect(checkoutProblem({ status: 503 }, echo, money)).toBe('checkout.unavailable');
  });

  it('keeps the server sentence for a request the shopper can correct', () => {
    const refused = {
      code: 'UnknownShippingOption',
      status: 400,
      title: 'That is not one of the delivery options for this address.',
      detail: "'express' is not offered here",
    };

    expect(checkoutProblem({ status: 400, error: refused }, echo, money)).toBe(
      `checkout.failed {"reason":"'express' is not offered here"}`,
    );
  });
});
