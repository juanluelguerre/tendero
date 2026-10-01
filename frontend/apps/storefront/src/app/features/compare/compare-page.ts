import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SpecSheet, SpecStatement } from '@tendero/shared-api';
import { Telemetry } from '@tendero/shared-telemetry';
import { ShopLinks } from '../../shop-links';
import { SpecSource } from '../../specs/spec-source/spec-source';
import { SpecValue } from '../../specs/spec-value/spec-value';
import { ComparisonStore } from './comparison.store';
import { Icon } from '@tendero/shared-ui';
import { signalMethod } from '@ngrx/signals';

/**
 * Two to four products side by side (P10-7): the same rows the product page
 * shows, aligned, each value with its source — and "no data" where a product
 * has none, said in words rather than left blank (ADR 0018).
 *
 * The URL is the comparison. The tray only builds it, so a comparison can be
 * reloaded, shared or opened by an agent that never saw the tray. The
 * comparison is its store's (ADR 0047); the page presents it and reports it.
 */
@Component({
  selector: 'storefront-compare-page',
  imports: [TranslocoDirective, RouterLink, SpecValue, SpecSource, Icon],
  providers: [ComparisonStore],
  templateUrl: './compare-page.html',
})
export class ComparePage {
  protected readonly store = inject(ComparisonStore);
  protected readonly links = inject(ShopLinks);

  private readonly telemetry = inject(Telemetry);

  constructor() {
    // Once per set of products compared: the computed's own equality is what keeps a
    // re-read of the same set from counting twice.
    signalMethod<string | null>((compared) => {
      if (compared) this.telemetry.trackEvent('products_compared', { products: compared.split(',').length });
    })(
      computed(() => {
        const current = this.store.state();
        return current.status === 'done' ? current.sheet.products.map((product) => product.code).join(',') : null;
      }),
    );
  }

  /** One product's sentences, which are not aligned: two sentences are not a row. */
  protected statementsOf(sheet: SpecSheet, productId: string): SpecStatement[] {
    return sheet.statements.filter((statement) => statement.productId === productId);
  }
}
