import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ProductSummary } from '@tendero/shared-api';

/** A product's status, and the languages it is missing, as the list and the pane both say them. No state. */
@Component({
  selector: 'backoffice-product-status-tags',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-status-tags.html',
  // Laid out by whoever places it; the tags are the shared ones (styles.css).
  host: { class: 'contents' },
})
export class ProductStatusTags {
  readonly product = input.required<ProductSummary>();
}
