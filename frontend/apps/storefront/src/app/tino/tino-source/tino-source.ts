import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { TinoReason } from '@tendero/shared-api';

/**
 * Where a sentence of Tino's comes from, as a chip under it (the Tino spec, *An
 * answer*). The sentence is the model's; the chip is the shop's — the source
 * and the fact the server returned with it, so what a shopper reads beside
 * "works on induction" is who says so, not the model's word for it.
 *
 * A claim a person approved opens the words it was approved on. Everything
 * else opens the fact as the shop holds it: the sentence can always be held
 * against what it cites.
 */
@Component({
  selector: 'storefront-tino-source',
  imports: [TranslocoDirective],
  templateUrl: './tino-source.html',
  host: { class: 'block' },
})
export class TinoSource {
  readonly reason = input.required<TinoReason>();

  protected readonly approved = computed(() => this.reason().source === 'Claim');

  /** How well the approved words support the claim, as a whole percentage; none when the shop holds no figure. */
  protected readonly percent = computed(() => {
    const support = this.reason().support;
    return typeof support === 'number' ? Math.round(support * 100) : null;
  });
}
