import { Component, inject } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Notice, StatusPill } from '@tendero/shared-ui';
import { ShopLinks } from '../../shop-links';
import { KitsStore } from './kits.store';

/**
 * The shop's kits (phase 13c, K-5): each template the shop wrote, with what
 * goes in it, as a way in to its page. A template is the shop's — three of
 * them, in a committed file — never something a model proposed.
 */
@Component({
  selector: 'storefront-kits',
  imports: [TranslocoDirective, RouterLink, FormField, Notice, StatusPill],
  providers: [KitsStore],
  templateUrl: './kits-page.html',
})
export class KitsPage {
  protected readonly links = inject(ShopLinks);
  protected readonly store = inject(KitsStore);

  protected understand(event: Event): void {
    event.preventDefault();
    void this.store.understand();
  }
}
