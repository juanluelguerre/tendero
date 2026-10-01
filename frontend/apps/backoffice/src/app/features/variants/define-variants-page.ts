import { Component, inject } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { DefineVariantsStore } from './define-variants.store';

/**
 * Declaring which axes a product varies by, and generating the matrix.
 *
 * It is a shopkeeper's screen, not an import one: a source may bring variants or
 * not, but deciding that a shirt sells in three colours by four sizes is a
 * catalogue decision.
 *
 * The whole cartesian product is generated on purpose. Retiring the three
 * combinations that do not exist afterwards costs less than creating the
 * twenty-one that do.
 */
@Component({
  selector: 'backoffice-define-variants',
  imports: [FormField, RouterLink, TranslocoDirective],
  templateUrl: './define-variants-page.html',
  providers: [DefineVariantsStore],
})
export class DefineVariantsPage {
  protected readonly store = inject(DefineVariantsStore);
  private readonly route = inject(ActivatedRoute);

  protected generate(): void {
    const productId = this.route.snapshot.paramMap.get('id');
    if (productId) this.store.generate(productId);
  }
}
