import { Component, inject, type OnInit } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Notice } from '@tendero/shared-ui';
import { ShopLinks } from '../../shop-links';
import { AccountNav } from './account-nav/account-nav';
import { AddressFormStore } from './address-form.store';

/**
 * One entry of the address book, added or edited (P15-2; board 03 of the
 * account's mock-ups): the kernel's address field for field, the shopper's label
 * for it, and the two defaults as boxes. Saving changes this entry only — an
 * order placed to it keeps the address it froze, and the line under the button
 * says so.
 */
@Component({
  selector: 'storefront-address-form-page',
  imports: [FormField, RouterLink, TranslocoDirective, Notice, AccountNav],
  providers: [AddressFormStore],
  templateUrl: './address-form-page.html',
})
export class AddressFormPage implements OnInit {
  protected readonly store = inject(AddressFormStore);
  protected readonly links = inject(ShopLinks);

  ngOnInit(): void {
    void this.store.start();
  }
}
