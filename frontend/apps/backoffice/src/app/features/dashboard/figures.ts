/**
 * The dashboard's reading aids, as pure functions (P11-7): how a figure moved
 * against the previous period, and a share written the reader's way. The numbers
 * themselves come from the server; these only say them.
 */

/** Up, down or flat against the previous period — and by how much, when there was a previous one to compare with. */
export interface Change {
  readonly direction: 'up' | 'down' | 'flat';
  /** The relative change, 0.25 for +25 %; null when the previous period had nothing, where a percentage means nothing. */
  readonly ratio: number | null;
}

export function changeOf(current: number, previous: number): Change {
  if (current === previous) return { direction: 'flat', ratio: previous === 0 ? null : 0 };
  const direction = current > previous ? 'up' : 'down';
  return { direction, ratio: previous === 0 ? null : (current - previous) / previous };
}

/** A share, 0 to 1, as the reader's culture writes a percentage: 0.1667 is "16,7 %" in Spanish and "16.7%" in English. */
export function formatShare(share: number, culture: string): string {
  return new Intl.NumberFormat(culture, { style: 'percent', maximumFractionDigits: 1 }).format(share);
}

/** A change as a signed percentage, "+25 %" / "−10 %", or null when there is none to state. */
export function formatChange(change: Change, culture: string): string | null {
  if (change.ratio === null) return null;
  return new Intl.NumberFormat(culture, { style: 'percent', maximumFractionDigits: 1, signDisplay: 'exceptZero' }).format(change.ratio);
}

/** One part of a whole, 0 when the whole is nothing. */
export function shareOf(part: number, whole: number): number {
  return whole === 0 ? 0 : part / whole;
}

/** At or under this, summed over the warehouses, a SKU is running low. A laboratory's shelf, not a policy. */
export const LowStockAt = 5;

/** What a shopper can still buy of each SKU, summed over the warehouses, and the ones at or under the line — lowest first. */
export function lowStockOf(rows: readonly { sku: string; available: number }[]): { sku: string; available: number }[] {
  const bySku = new Map<string, number>();
  for (const row of rows) bySku.set(row.sku, (bySku.get(row.sku) ?? 0) + row.available);
  return [...bySku]
    .map(([sku, available]) => ({ sku, available }))
    .filter((sku) => sku.available <= LowStockAt)
    .sort((a, b) => a.available - b.available || a.sku.localeCompare(b.sku));
}
