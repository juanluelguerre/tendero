import { DOCUMENT } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthStore } from '@tendero/shared-auth';
import { Notice } from '@tendero/shared-ui';
import { ShopLinks } from '../../shop-links';
import { AccountNav } from './account-nav/account-nav';
import { YourDataStore } from './your-data.store';

/**
 * The shopper's own data (P15-9; board 06 of the account's mock-ups): see what the
 * shop holds, and remove the account — what is forgotten, what is kept and used
 * for nothing else, and what is not the shop's to remove. Removing is red and
 * behind a confirmation; nothing on this page is clay.
 */
@Component({
  selector: 'storefront-your-data-page',
  imports: [RouterLink, TranslocoDirective, Notice, AccountNav],
  providers: [YourDataStore],
  templateUrl: './your-data-page.html',
})
export class YourDataPage {
  protected readonly store = inject(YourDataStore);
  protected readonly links = inject(ShopLinks);
  private readonly auth = inject(AuthStore);
  private readonly document = inject(DOCUMENT);

  protected readonly signedIn = this.auth.isSignedIn;

  /** Asking before removing: lives only while it is asked. */
  protected readonly confirming = signal(false);

  /** The download handed to the browser as a file: the DOM's business, so the page's and not the store's. */
  protected async download(): Promise<void> {
    const data = await this.store.download();
    if (!data) return;

    const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }));
    const link = this.document.createElement('a');
    link.href = url;
    link.download = `tendero-your-data-${data.generatedAt.slice(0, 10)}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }

  protected signOut(): void {
    this.auth.signOut();
  }
}
