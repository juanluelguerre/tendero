import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthStore } from '@tendero/shared-auth';
import { ProductCard } from '../../product-card/product-card';
import { ShopLinks } from '../../shop-links';
import { TrailStore } from '../trail.store';
import { TrailHistoryStore } from './trail-history.store';
import { Icon, ToggleSwitch } from '@tendero/shared-ui';

/**
 * *Tu historial*, in *Tu cuenta* (R-8, H-3b; docs/privacy.md): the whole trail,
 * removable one entry at a time and all at once, and the switch that turns it
 * off — after which nothing is kept, not even in this browser.
 *
 * A guest has it too, on the same page below *Identifícate*: their trail is this
 * browser's, and the controls over it are theirs as much as a customer's.
 * What it reads is its store's (ADR 0047).
 */
@Component({
  selector: 'storefront-trail-history',
  imports: [TranslocoDirective, RouterLink, ProductCard, Icon, ToggleSwitch],
  providers: [TrailHistoryStore],
  templateUrl: './trail-history.html',
  host: { class: 'block' },
})
export class TrailHistory {
  protected readonly store = inject(TrailHistoryStore);
  protected readonly trail = inject(TrailStore);
  protected readonly links = inject(ShopLinks);
  protected readonly signedIn = inject(AuthStore).isSignedIn;
}
