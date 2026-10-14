import { describe, expect, it } from 'vitest';
import { formatPrice, majorUnits, minorUnits } from './format-price';

/**
 * Money arrives as the wire carries it (phase 13b): a whole number of the
 * currency's minor units and the currency. Turning that into text is the
 * front end's half of the contract, and the one place it can go wrong by a
 * factor of a hundred.
 */
describe('a price, formatted', () => {
  const written = (amount: number, currency: string, culture: string) =>
    new Intl.NumberFormat(culture, { style: 'currency', currency }).format(amount);

  it('is the minor units as the language writes that money', () => {
    expect(formatPrice({ amount: 123450, currency: 'EUR' }, 'es')).toBe(written(1234.5, 'EUR', 'es'));
    expect(formatPrice({ amount: 123450, currency: 'EUR' }, 'en')).toBe(written(1234.5, 'EUR', 'en'));
    expect(formatPrice({ amount: 5, currency: 'EUR' }, 'es')).toBe(written(0.05, 'EUR', 'es'));
  });

  /**
   * The currency's own minor unit, not a hundredth: the yen has none and the
   * Kuwaiti dinar three. The browser's `Intl` already knows each one, so there
   * is no table here to fall out of step with the server's.
   */
  it("divides by the currency's own minor unit", () => {
    expect(formatPrice({ amount: 3900, currency: 'JPY' }, 'en')).toBe(written(3900, 'JPY', 'en'));
    expect(formatPrice({ amount: 1250, currency: 'KWD' }, 'en')).toBe(written(1.25, 'KWD', 'en'));
  });

  it('gives the amount in major units to whoever needs a number, not a text', () => {
    expect(majorUnits({ amount: 11390, currency: 'EUR' })).toBe(113.9);
    expect(majorUnits({ amount: 3900, currency: 'JPY' })).toBe(3900);
    expect(majorUnits({ amount: 1250, currency: 'KWD' })).toBe(1.25);
  });

  /**
   * The way back, for what a person typed and the API is asked: a price filter
   * goes out in the unit prices come in. Rounded, because 59.9 × 100 is
   * 5989.999… in a float and the API refuses a fraction.
   */
  it('gives a typed amount in minor units, whole, by the currency', () => {
    expect(minorUnits(59.9, 'EUR')).toBe(5990);
    expect(minorUnits(30, 'EUR')).toBe(3000);
    expect(minorUnits(3900, 'JPY')).toBe(3900);
    expect(minorUnits(1.25, 'KWD')).toBe(1250);
  });
});
