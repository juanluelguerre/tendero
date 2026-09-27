import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SpecCell } from '@tendero/shared-api';

/**
 * One spec value as a shopper reads it: typed by the API, phrased here. "Sí",
 * "Yes" and "sin dato" are interface copy and live in the translation files
 * (invariant 6), so the product page and the comparison say them the same way.
 */
@Component({
  selector: 'storefront-spec-value',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './spec-value.html',
  styleUrl: './spec-value.css',
})
export class SpecValue {
  readonly cell = input.required<SpecCell>();
}
