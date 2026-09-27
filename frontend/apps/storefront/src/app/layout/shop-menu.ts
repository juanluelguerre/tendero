import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Taxonomy } from '../data-access/taxonomy';
import { ShopLinks } from '../shop-links';

/**
 * The menu the header's TIENDA always looked like it was (the redesign's R-3,
 * S-P1; R-1, S-F1 and S-F6): every department and category, two interactions
 * from any page. A panel of columns on a desktop, one per department with its
 * sections as headings; a flat list in the phone's drawer, every category
 * visible, because an accordion would make it three.
 *
 * Its links say they came from the menu in their navigation state, so the
 * category page can tell `category_opened` it was reached this way (R-0).
 */
@Component({
  selector: 'storefront-shop-menu',
  imports: [RouterLink, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shop-menu.html',
  styleUrl: './shop-menu.css',
  host: { '[class.in-drawer]': "mode() === 'drawer'" },
})
export class ShopMenu {
  readonly mode = input<'panel' | 'drawer'>('panel');
  /** The department the shopper is in, marked where it is listed. */
  readonly current = input<string | null>(null);
  readonly chosen = output<void>();

  protected readonly menu = inject(Taxonomy).menu;
  protected readonly links = inject(ShopLinks);
  protected readonly fromMenu = { via: 'menu' };
}
