import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { PageState } from '../../page-state/page-state';
import { ShopLinks } from '../../shop-links';

/**
 * An address the shop has no page for, inside a language (`/es/x`). It answers
 * at the address asked for, so the link that was wrong is still visible, and it
 * offers the way back rather than a dead end.
 *
 * Before it, such an address fell through to the redirect for paths with no
 * language, which added the language again — `/es/es/x`, and on for ever — and
 * the tab froze (`app.routes.spec.ts`).
 */
@Component({
  selector: 'storefront-not-found-page',
  imports: [TranslocoDirective, RouterLink, PageState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './not-found-page.html',
  host: { class: 'block' },
})
export class NotFoundPage {
  protected readonly links = inject(ShopLinks);
}
