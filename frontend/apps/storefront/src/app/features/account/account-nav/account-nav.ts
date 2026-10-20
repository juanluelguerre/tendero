import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ShopLinks } from '../../../shop-links';

/** The account's sections, as the menu names them. A closed set. */
export type AccountSection = 'orders' | 'addresses' | 'waysToPay' | 'preferences' | 'yourData';

/**
 * The account's menu (P15-7; boards 01 and 10 of the account's mock-ups), built
 * last, as the backlog decided on 2026-09-04: a menu with one item is chrome
 * pretending to be structure, and the account has five sections now.
 *
 * On a wide screen it is the sidebar beside each section. On a phone the account's
 * first page shows it as a list of the sections, each its own page with a way
 * back, and the section pages leave it out (`max-md:hidden` from the caller).
 */
@Component({
  selector: 'storefront-account-nav',
  imports: [RouterLink, TranslocoDirective],
  templateUrl: './account-nav.html',
  host: { class: 'block' },
})
export class AccountNav {
  private readonly links = inject(ShopLinks);

  /** The section on screen, drawn as the current one. */
  readonly active = input<AccountSection | null>(null);

  protected readonly sections = computed(() => [
    { key: 'orders' as const, link: this.links.account() },
    { key: 'addresses' as const, link: this.links.addresses() },
    { key: 'waysToPay' as const, link: this.links.waysToPay() },
    { key: 'preferences' as const, link: this.links.preferences() },
    { key: 'yourData' as const, link: this.links.yourData() },
  ]);
}
