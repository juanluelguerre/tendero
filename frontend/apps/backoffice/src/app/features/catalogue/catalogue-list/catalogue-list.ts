import { PricePipe } from '@tendero/shared-ui';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ImportResult, ProductSummary } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { FilterChips } from '../../../worklist/filter-chips/filter-chips';
import { Find } from '../../../worklist/find/find';
import { ListState } from '../../../worklist/list-state/list-state';
import { Pager } from '../../../worklist/pager/pager';
import { CatalogueExports } from '../catalogue-exports/catalogue-exports';
import { CatalogueStore } from '../catalogue.store';
import { ProductStatusTags } from '../product-status-tags/product-status-tags';

/**
 * The products, narrowed by status and by what is typed, each with the way to
 * its variants — and importing the seed, a developer's action kept quiet here
 * (R-4.3). It reads and acts through the page's store, which the pane shares.
 */
@Component({
  selector: 'backoffice-catalogue-list',
  imports: [
    RouterLink,
    TranslocoDirective,
    FilterChips,
    Pager,
    ProductStatusTags,
    Find,
    ListState,
    PricePipe,
    CatalogueExports,
  ],
  templateUrl: './catalogue-list.html',
  styleUrl: './catalogue-list.css',
})
export class CatalogueList {
  protected readonly store = inject(CatalogueStore);
  protected readonly culture = inject(CultureStore);

  protected readonly pageSize = this.store.pageSize;

  protected seconds(result: ImportResult): string {
    return result.elapsedSeconds.toFixed(1);
  }
}
