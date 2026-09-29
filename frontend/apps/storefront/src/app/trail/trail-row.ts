import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { ProductRow, type ShelfItem } from '../shelves/product-row';
import { ShopLinks } from '../shop-links';
import { Trail } from './trail';

/**
 * *Sigue donde lo dejaste* (R-8, H-3): the products the shopper viewed and the
 * searches they made, most recent first — the row every large shop puts in its
 * first or second band, because it is the one that needs no choosing.
 *
 * Each can be removed, and *Borrar historial* removes them all; *Gestionar* leads
 * to *Tu historial*, where the switch is. The row is the shared `ProductRow`,
 * with the searches as chips above the cards.
 */
@Component({
  selector: 'storefront-trail-row',
  imports: [TranslocoDirective, RouterLink, ProductRow],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trail-row.html',
  styleUrl: './trail-row.css',
})
export class TrailRow {
  /** The viewed products' cards, in the trail's order. */
  readonly viewed = input.required<readonly SearchHit[]>();

  protected readonly trail = inject(Trail);
  protected readonly links = inject(ShopLinks);
  protected readonly signedIn = inject(AuthStore).isSignedIn;

  protected readonly items = computed<ShelfItem[]>(() => this.viewed().map((hit) => ({ hit })));
  protected readonly searches = computed(() => this.trail.shown().searches);
}
