import type { PriceChangedResponse } from '@tendero/shared-api';

/**
 * One sentence a shopper can act on, per status code.
 *
 * The three that matter are told apart deliberately. A **402** is the card
 * saying no — said in the shop's language, and WITHOUT the provider's reason:
 * the provider speaks one language and the shop two, and quoting it produced
 * "La tarjeta ha sido rechazada: The card was declined by the issuer." (UI
 * review, 2026-09-11). The reason is not lost; the audit row keeps it verbatim
 * for the shopkeeper. A **503** is the provider being unreachable, which is
 * retryable and is not the shopper's fault. A **409** is the price having
 * moved, and it carries the new total, because "try again" without saying what
 * changed is how a checkout loses a sale it had already made.
 */
export function checkoutProblem(
  failure: { status?: number; error?: unknown },
  translate: (key: string, params?: Record<string, unknown>) => string,
  money: (amount: number) => string,
): string {
  if (failure.status === 409) {
    const changed = failure.error as PriceChangedResponse | null;
    return translate('checkout.priceChanged', { total: money(changed?.total ?? 0) });
  }

  if (failure.status === 402) return translate('checkout.declined');

  if (failure.status === 503) return translate('checkout.unavailable');

  // 400 and 404 come back as a plain string written for a shopper — "'express'
  // is not one of the delivery options for this address" — and replacing that
  // with a generic message would throw away the useful half.
  const problem = failure.error as { detail?: string } | null;
  const detail =
    typeof failure.error === 'string' && failure.error.length > 0
      ? failure.error
      : (problem?.detail ?? '');

  return translate('checkout.failed', { reason: detail });
}
