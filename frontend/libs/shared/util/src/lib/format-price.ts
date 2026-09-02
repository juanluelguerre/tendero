/**
 * Price formatting for both apps. It was written identically in the storefront's
 * search page and in the backoffice's review queue.
 *
 * It is shared because it is BEHAVIOUR, not identity (ADR 0010): thousands
 * grouping, symbol position and decimal separator all change with the culture,
 * and the browser's Intl already knows how. What is NOT shared is how the price
 * LOOKS — that belongs to each surface, and lives in its CSS.
 */
export function formatPrice(amount: number, currency: string, culture: string): string {
  return new Intl.NumberFormat(culture, { style: 'currency', currency }).format(amount);
}
