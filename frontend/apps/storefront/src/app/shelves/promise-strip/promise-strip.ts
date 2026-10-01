import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Icon } from '@tendero/shared-ui';

/**
 * *La tienda promete* (R-8, H-4): a slim strip of what the shop commits to.
 *
 * **Every number is read from the rule that holds it, never typed here.** The
 * free-shipping line is the shipping promotion's own name (*Envío gratis desde
 * 50 €*, from `/api/pricing/offers`), and the returns line is the window
 * `/api/returns/policy` answers. Either unknown, its line is not drawn — a
 * promise the shop cannot back is worse than no promise.
 */
@Component({
  selector: 'storefront-promise-strip',
  imports: [TranslocoDirective, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './promise-strip.html',
  host: { class: 'block' },
})
export class PromiseStrip {
  /** The free-shipping promotion's name, as the shop sells it; null when there is none. */
  readonly freeShipping = input<string | null>(null);
  /** Days to return from delivery; null until known. */
  readonly returnDays = input<number | null>(null);
}
