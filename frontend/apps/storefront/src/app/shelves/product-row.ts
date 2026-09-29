import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  effect,
  type ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink, type Params } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import type { ProductVia } from '../features/product/arrived-from';
import { ProductCard, type CardReason } from '../product-card';

/** One product in a row, with what the row says about it. */
export interface ShelfItem {
  readonly hit: SearchHit;
  readonly badge?: string | null;
  readonly reason?: CardReason | null;
  readonly rank?: number | null;
}

/** *Ver todas*: where the whole list is, with its words. */
export interface ShelfLink {
  readonly label: string;
  readonly commands: readonly unknown[];
  readonly queryParams?: Params;
}

let rows = 0;

/**
 * A row of products that scrolls sideways — every row of the home page (R-8):
 * the trail, the offers, the suggestions, what rises, what sells, what is new.
 *
 * **One component, so one set of rules** (design/DESIGN.md): whole cards on a
 * desktop with two buttons that stop at the ends, the next card peeking in on a
 * phone, snap under the hand and never on a timer. It was written once for
 * *Novedades* and would otherwise be copied six times.
 *
 * **The cards load when the row is near the screen** (`@defer (on viewport)`),
 * with a skeleton where they will be, so the first screen weighs what it shows
 * and not what is below it (the spec's weight measure).
 *
 * Two slots: `[shelfActions]` beside the heading (*Borrar historial*) and
 * `[shelfExtras]` above the cards (the trail's searches, as chips).
 */
@Component({
  selector: 'storefront-product-row',
  imports: [TranslocoDirective, RouterLink, ProductCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-row.html',
  styleUrl: './product-row.css',
})
export class ProductRow {
  readonly heading = input.required<string>();
  readonly lede = input<string | null>(null);
  readonly more = input<ShelfLink | null>(null);
  readonly items = input.required<readonly ShelfItem[]>();
  /** The row, for `product_viewed.arrived_from`; null for rows the page's URL already names. */
  readonly via = input<ProductVia | null>(null);
  readonly layout = input<'tile' | 'line'>('tile');
  /** Offers a × on each card, which emits `removed` — the trail's products. */
  readonly removable = input(false);

  readonly removed = output<SearchHit>();

  protected readonly id = `shelf-${++rows}`;
  protected readonly atStart = signal(true);
  protected readonly atEnd = signal(true);

  private readonly row = viewChild<ElementRef<HTMLElement>>('row');

  constructor() {
    const injector = inject(Injector);
    // The buttons know the ends once the cards are drawn, and again when their number changes.
    effect(() => {
      this.items();
      const row = this.row()?.nativeElement;
      if (row) afterNextRender(() => this.edges(row), { injector });
    });
  }

  /** A page of the row: as far as it shows, so the next whole cards come in. */
  protected page(direction: 1 | -1): void {
    const row = this.row()?.nativeElement;
    row?.scrollBy({ left: direction * row.clientWidth, behavior: 'smooth' });
  }

  protected edges(row: HTMLElement): void {
    this.atStart.set(row.scrollLeft <= 1);
    this.atEnd.set(row.scrollLeft + row.clientWidth >= row.scrollWidth - 1);
  }

  /** Four skeleton cards while the row is below the screen. */
  protected readonly placeholders = [1, 2, 3, 4];
}
