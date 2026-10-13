import type { AppliedDiscount, Money } from '@tendero/shared-api';

/** What a sentence is written with: the page's words, its money, and the names it has. */
export interface ReasonWords {
  t: (key: string, params?: Record<string, unknown>) => string;
  money: (amount: Money) => string;
  /** A category's name by its code, when the page knows it. */
  category: (code: string) => string | null;
  /** An article's name by its SKU, when the basket holds it. */
  product: (sku: string) => string | null;
}

/**
 * Why a promotion did not apply, in the page's own sentence (ADR 0059).
 *
 * The answer carries a code, what the promotion asked for, and how far the
 * basket is from it; the page composes the sentence, so money reads the way
 * this reader reads money and a category is called by its name. The server's
 * sentence is what an agent reads — and what the page falls back on for a
 * reason it has no words for, so a new code is never a blank line.
 */
export function discountReason(discount: AppliedDiscount, words: ReasonWords): string | null {
  if (discount.outcome === 'Applied') return null;

  const { t } = words;

  switch (discount.reasonCode) {
    case 'NotCombinableWith':
      return discount.winner ? t('cart.reason.notCombinableWith', { winner: discount.winner }) : discount.reason;
    case 'SupersededByExclusive':
      return discount.winner ? t('cart.reason.supersededByExclusive', { winner: discount.winner }) : discount.reason;
    case 'NothingToDiscount':
      return t('cart.reason.nothingToDiscount');
    case 'ConditionNotMet':
      return discount.requires ? conditionNotMet(discount.requires, words) : discount.reason;
    default:
      return discount.reason;
  }
}

function conditionNotMet(
  asked: NonNullable<AppliedDiscount['requires']>,
  { t, money, category, product }: ReasonWords,
) {
  const what = [
    asked.minimumSubtotal && t('cart.reason.minimum', { amount: money(asked.minimumSubtotal) }),
    asked.minimumQuantity != null && t('cart.reason.quantity', { count: asked.minimumQuantity }),
    asked.categoryCode && t('cart.reason.category', { name: category(asked.categoryCode) ?? asked.categoryCode }),
    asked.sku && t('cart.reason.article', { name: product(asked.sku) ?? asked.sku }),
  ].filter((part): part is string => !!part);

  const short = [
    asked.shortBy && t('cart.reason.shortBy', { amount: money(asked.shortBy) }),
    asked.unitsShort === 1 && t('cart.reason.oneUnitShort'),
    asked.unitsShort != null && asked.unitsShort > 1 && t('cart.reason.unitsShort', { count: asked.unitsShort }),
  ].filter((part): part is string => !!part);

  return [
    t('cart.reason.conditionNotMet', {
      what: what.length ? what.join(t('cart.reason.and')) : t('cart.reason.anything'),
    }),
    ...short,
  ].join(' ');
}
