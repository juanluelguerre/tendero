import type { CheckoutConflict, Money } from '@tendero/shared-api';
import { refusalSentence } from '@tendero/shared-util';

/**
 * One sentence a shopper can act on, per status code.
 *
 * The three that matter are told apart deliberately. A **402** is the card
 * saying no — said in the shop's language, and WITHOUT the provider's reason:
 * the provider speaks one language and the shop two, and quoting it produced
 * "La tarjeta ha sido rechazada: The card was declined by the issuer." (UI
 * review, 2026-09-11). The reason is not lost; the audit row keeps it verbatim
 * for the shopkeeper. A **503** is the provider being unreachable, which is
 * retryable and is not the shopper's fault. A **409** is told apart by its
 * code: the price having moved carries the new total, because "try again"
 * without saying what changed is how a checkout loses a sale it had already
 * made; the shelf being short carries what there is of each article.
 */
export function checkoutProblem(
  failure: { status?: number; error?: unknown },
  translate: (key: string, params?: Record<string, unknown>) => string,
  money: (amount: Money) => string,
  product: (sku: string) => string | null = () => null,
): string {
  if (failure.status === 409) {
    // The body is whatever the server sent: what a code carries is read only when it is there.
    const conflict = failure.error as CheckoutConflict | null;
    const code = conflict?.code;

    if (code === 'PriceChanged' && conflict && 'total' in conflict && conflict.total)
      return translate('checkout.priceChanged', { total: money(conflict.total) });

    if (code === 'InsufficientStock' && conflict && 'shortages' in conflict && conflict.shortages?.length) {
      const what = conflict.shortages.map((line) => {
        const name = product(line.sku) ?? line.sku;
        if (line.available <= 0) return translate('checkout.noneOf', { name });

        return line.available === 1
          ? translate('checkout.oneOf', { name })
          : translate('checkout.shortOf', { name, available: line.available });
      });

      return translate('checkout.insufficientStock', { what: what.join(translate('checkout.shortAnd')) });
    }
  }

  if (failure.status === 402) return translate('checkout.declined');

  if (failure.status === 503) return translate('checkout.unavailable');

  // 400 and 404 come back as a problem whose sentence the shop wrote for a
  // shopper, in their language — "«express» no se ofrece para esta dirección" —
  // and replacing that with a generic message would throw away the useful half.
  return translate('checkout.failed', { reason: refusalSentence(failure.error) });
}
