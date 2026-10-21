import { Component, inject, type OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { DayPipe, Notice, ToggleSwitch } from '@tendero/shared-ui';
import { ShopLinks } from '../../shop-links';
import { AccountNav } from './account-nav/account-nav';
import { PreferencesStore } from './preferences.store';

/**
 * The shopper's preferences (P15-6; board 05 of the account's mock-ups): three,
 * and no currency. Each change saves at once; nothing here is an action that
 * wants pressing, so nothing is clay.
 */
@Component({
  selector: 'storefront-preferences-page',
  imports: [RouterLink, TranslocoDirective, Notice, DayPipe, AccountNav, ToggleSwitch],
  providers: [PreferencesStore],
  templateUrl: './preferences-page.html',
})
export class PreferencesPage implements OnInit {
  protected readonly store = inject(PreferencesStore);
  protected readonly links = inject(ShopLinks);
  protected readonly signedIn = inject(AuthStore).isSignedIn;
  protected readonly culture = inject(CultureStore).active;

  protected readonly languages = inject(CultureStore).available;

  ngOnInit(): void {
    void this.store.load();
  }
}
