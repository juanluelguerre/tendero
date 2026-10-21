import { Component, inject, signal, type OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SavedMethodView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { Notice } from '@tendero/shared-ui';
import { ShopLinks } from '../../shop-links';
import { AccountNav } from './account-nav/account-nav';
import { KeepableCards, keepableCardKey, type KeepableCard } from '../../wallet/test-cards';
import { WalletStore } from '../../wallet/wallet.store';

/**
 * The shopper's saved ways to pay (P15-4; board 04 of the account's mock-ups):
 * each card as what may be shown of it, the default, an expired one said to be
 * one, and adding a card through the provider's own field — the fake's test
 * cards here.
 *
 * What is being added and which card is being confirmed for removal live only
 * while they are chosen: component signals, not the wallet's state.
 */
@Component({
  selector: 'storefront-ways-to-pay-page',
  imports: [RouterLink, TranslocoDirective, Notice, AccountNav],
  templateUrl: './ways-to-pay-page.html',
})
export class WaysToPayPage implements OnInit {
  protected readonly wallet = inject(WalletStore);
  protected readonly links = inject(ShopLinks);
  protected readonly signedIn = inject(AuthStore).isSignedIn;

  protected readonly cards = KeepableCards;
  protected readonly cardKey = keepableCardKey;

  /** The add-a-card panel, open or not. */
  protected readonly adding = signal(false);
  protected readonly card = signal<KeepableCard>(KeepableCards[0]);
  protected readonly makeDefault = signal(false);

  /** The method whose removal is being asked about; null when nothing is. */
  protected readonly confirming = signal<SavedMethodView | null>(null);

  ngOnInit(): void {
    void this.wallet.load();
  }

  protected async keep(): Promise<void> {
    if (await this.wallet.keep(this.card(), this.makeDefault())) {
      this.adding.set(false);
      this.makeDefault.set(false);
    }
  }

  protected async remove(method: SavedMethodView): Promise<void> {
    if (await this.wallet.remove(method.id)) this.confirming.set(null);
  }
}
