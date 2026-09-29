import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { map } from 'rxjs';
import { ProductSearchService } from '../data-access/product-search.service';
import { ProductCard } from '../product-card';
import { ShopLinks } from '../shop-links';
import { hasTrail } from './trail-rules';
import { Trail } from './trail';

/**
 * *Tu historial*, in *Tu cuenta* (R-8, H-3b; docs/privacy.md): the whole trail,
 * removable one entry at a time and all at once, and the switch that turns it
 * off — after which nothing is kept, not even in this browser.
 *
 * A guest has it too, on the same page below *Identifícate*: their trail is this
 * browser's, and the controls over it are theirs as much as a customer's.
 */
@Component({
  selector: 'storefront-trail-history',
  imports: [TranslocoDirective, RouterLink, ProductCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trail-history.html',
  styleUrl: './trail-history.css',
})
export class TrailHistory {
  private readonly search = inject(ProductSearchService);
  private readonly culture = inject(CultureStore);

  protected readonly trail = inject(Trail);
  protected readonly links = inject(ShopLinks);
  protected readonly signedIn = inject(AuthStore).isSignedIn;

  protected readonly kept = this.trail.kept;
  protected readonly any = computed(() => hasTrail(this.kept()));

  private readonly codes = computed(() => this.kept().views.map((entry) => entry.key));

  private readonly cards = rxResource({
    params: () => (this.codes().length ? { culture: this.culture.active(), codes: this.codes() } : undefined),
    stream: ({ params }) => this.search.cards(params.culture, { codes: params.codes }).pipe(map((result) => result.items)),
  });

  /** The viewed products as cards, in the trail's order; a product no longer shown is simply not listed. */
  protected readonly viewed = computed<readonly SearchHit[]>(() => (this.cards.hasValue() ? this.cards.value() : []));

  protected toggle(event: Event): void {
    this.trail.setEnabled((event.target as HTMLInputElement).checked);
  }
}
