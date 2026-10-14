import type { KitSlotView, Money } from '@tendero/shared-api';
import { majorUnits } from '@tendero/shared-util';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/**
 * What the kit says of one slot, or nothing when there is nothing to say — a
 * slot that got the best there was needs no sentence.
 *
 * Composed here, from what the answer carries (ADR 0059; decided on the
 * mock-up): the reason and the amounts are the server's, worked out by the
 * solver; the words and the way money reads are the page's. The server's own
 * sentences (`KitView.explanation`) are for a reader with no page — an agent.
 */
export function slotSentence(slot: KitSlotView, t: Translate, money: (amount: Money) => string): string | null {
  if (slot.gap) {
    const { reason, cheapest, short } = slot.gap;
    return reason === 'OverBudget' && cheapest && short
      ? t('kits.said.overBudget', { price: money(cheapest.price), short: money(short) })
      : t('kits.said.noCandidate');
  }

  return slot.better
    ? t('kits.said.better', { name: slot.better.product.name, short: money(slot.better.short) })
    : null;
}

/** What adding the kit puts in the basket: one of each product chosen. An empty slot adds nothing. */
export function kitLines(slots: readonly KitSlotView[]): { sku: string; quantity: number }[] {
  return slots.flatMap((slot) => (slot.product ? [{ sku: slot.product.matchedSku, quantity: 1 }] : []));
}

/**
 * How much of the budget the kit spends, as a percentage for the bar under the
 * total. A proportion drawn, not an amount shown: every figure a shopper reads
 * is the server's.
 */
export function spentShare(total: Money, budget: Money): number {
  const all = majorUnits(budget);
  return all > 0 ? Math.min(100, (majorUnits(total) / all) * 100) : 0;
}

export type BudgetBand = 'under_50' | '50_100' | '100_200' | '200_500' | '500_plus';

/** The band `kit_built` reports a budget in (the tracking plan): what people mean to spend, never the figure. */
export function budgetBand(budget: Money): BudgetBand {
  const amount = majorUnits(budget);
  if (amount < 50) return 'under_50';
  if (amount < 100) return '50_100';
  if (amount < 200) return '100_200';
  if (amount < 500) return '200_500';
  return '500_plus';
}
