/**
 * Money as the API carries it (phase 13b, `docs/specs/wire-contract.md`): a
 * whole number of the currency's **minor units** and the currency — 113,90 €
 * is `{ amount: 11390, currency: 'EUR' }`. Declared here by shape, because
 * this library is below the one that holds the generated types; the generated
 * `Money` is assignable to it.
 */
export interface MoneyAmount {
  readonly amount: number;
  readonly currency: string;
}

/**
 * How many minor units make one of a currency: a hundred for the euro, one for
 * the yen, a thousand for the dinar. **Asked of the browser, not written
 * here** — `Intl` knows ISO 4217, so there is no second table to fall out of
 * step with the server's.
 */
function minorUnitsIn(currency: string): number {
  const digits = new Intl.NumberFormat('en', { style: 'currency', currency }).resolvedOptions().maximumFractionDigits;
  return 10 ** (digits ?? 2);
}

/**
 * The amount as a number in major units (113.9), for the few places that need
 * one and not a text: a chart's axis, a product's structured data. Anything a
 * person reads goes through {@link formatPrice}.
 */
export function majorUnits(money: MoneyAmount): number {
  return money.amount / minorUnitsIn(money.currency);
}

/**
 * The way back: an amount a person typed, in major units (59.9), as the whole
 * number of minor units the API is asked in (5990) — a price filter goes out
 * in the unit prices come in. Rounded, because 59.9 × 100 is not 5990 in a
 * float and the API refuses a fraction.
 */
export function minorUnits(amount: number, currency: string): number {
  return Math.round(amount * minorUnitsIn(currency));
}

/**
 * The currency the shop sells in, for the few places that have to name one
 * before any answer has: a price filter read from an address, a figure drawn
 * while its data is still on the way. The server's `ShopOptions.DefaultCurrency`;
 * everywhere an answer is in hand, the currency is the answer's own.
 */
export const ShopCurrency = 'EUR';

/**
 * Price formatting for both apps. It was written identically in the storefront's
 * search page and in the backoffice's review queue.
 *
 * It is shared because it is BEHAVIOUR, not identity (ADR 0010): thousands
 * grouping, symbol position and decimal separator all change with the culture,
 * and the browser's Intl already knows how. What is NOT shared is how the price
 * LOOKS — that belongs to each surface, and lives in its CSS.
 */
export function formatPrice(money: MoneyAmount, culture: string): string {
  return formatMajor(majorUnits(money), money.currency, culture);
}

/**
 * A number **already in major units**, written as money: a chart's axis tick
 * or a bar's figure, which the chart computed from the values it was given
 * (those of {@link majorUnits}) and hands back as a bare number. Nothing the
 * API sent goes through here — that is {@link formatPrice}'s.
 */
export function formatMajor(amount: number, currency: string, culture: string): string {
  return new Intl.NumberFormat(culture, { style: 'currency', currency }).format(amount);
}
