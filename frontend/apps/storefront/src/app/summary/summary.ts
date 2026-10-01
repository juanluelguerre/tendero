import { Component } from '@angular/core';

/**
 * The panel an order's figures sit in beside its lines — the cart, the checkout,
 * the order. It sticks under the header while the lines scroll: the total is
 * what a shopper checks while editing them, and scrolling to find it is how a
 * basket gets abandoned. It lets go on a tablet or a phone, where it sits under
 * the lines and sticking would cover them.
 *
 * Written three times until the review of 2026-10-01 (ADR 0049, point 4: what
 * repeats becomes a component). The host takes no box, so the aside is the
 * page grid's own child, which is what `sticky` needs.
 */
@Component({
  selector: 'storefront-summary',
  templateUrl: './summary.html',
  host: { class: 'contents' },
})
export class Summary {}
