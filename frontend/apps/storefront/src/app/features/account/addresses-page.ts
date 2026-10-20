import { Component, inject, signal, type OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AddressEntryView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { Notice } from '@tendero/shared-ui';
import { AddressBookStore } from '../../addresses/address-book.store';
import { ShopLinks } from '../../shop-links';
import { AccountNav } from './account-nav/account-nav';

/**
 * The shopper's address book (P15-2; board 02 of the account's mock-ups): the
 * entries in the order they were added, which is each one's default, and the
 * way to add, edit, remove and choose.
 *
 * **Removing asks first, and says what it means.** A default removed leaves none —
 * nothing takes its place, and the next checkout asks — so the confirmation says
 * that before it happens, and that orders already placed keep their address.
 * Which entry is being confirmed lives only while it is asked: a component
 * signal, not the book's state.
 */
@Component({
  selector: 'storefront-addresses-page',
  imports: [RouterLink, TranslocoDirective, Notice, AccountNav],
  templateUrl: './addresses-page.html',
})
export class AddressesPage implements OnInit {
  protected readonly book = inject(AddressBookStore);
  protected readonly links = inject(ShopLinks);
  protected readonly signedIn = inject(AuthStore).isSignedIn;

  /** The entry whose removal is being asked about; null when nothing is. */
  protected readonly confirming = signal<AddressEntryView | null>(null);

  ngOnInit(): void {
    void this.book.load();
  }

  protected async remove(entry: AddressEntryView): Promise<void> {
    if (await this.book.remove(entry.id)) this.confirming.set(null);
  }
}
