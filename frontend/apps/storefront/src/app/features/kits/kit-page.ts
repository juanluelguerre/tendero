import { Component, effect, inject } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { KitSlotView, SearchHit } from '@tendero/shared-api';
import { CultureStore, PageTitles } from '@tendero/shared-i18n';
import { Notice, PricePipe, StatusPill, ToggleSwitch } from '@tendero/shared-ui';
import { formatPrice, ProductImageUrls } from '@tendero/shared-util';
import { ProductImage } from '../../product-image/product-image';
import { ShopLinks } from '../../shop-links';
import { slotSentence, spentShare } from './kit-sentences';
import { KitStore } from './kit.store';

/**
 * One kit (phase 13c, K-5; the mock-up `storefront-kits`): on the left what
 * the shopper gives — a budget, and what the kit has to work with — and on the
 * right what the kit is made of, then what the shop built.
 *
 * **One row a slot, in the template's order**, each with the shop's own
 * product — the data a search result shows, never a second kind of card — and
 * a sentence only where there is something to say. An empty slot keeps its
 * row and says why. The page's one clay action is *Build* until there is a
 * kit, and *add it to the basket* after.
 */
@Component({
  selector: 'storefront-kit',
  imports: [TranslocoDirective, RouterLink, FormField, Notice, StatusPill, ProductImage, PricePipe, ToggleSwitch],
  providers: [KitStore],
  templateUrl: './kit-page.html',
})
export class KitPage {
  protected readonly links = inject(ShopLinks);
  protected readonly store = inject(KitStore);
  protected readonly culture = inject(CultureStore);
  private readonly images = inject(ProductImageUrls);

  protected readonly spentShare = spentShare;

  constructor() {
    const titles = inject(PageTitles);

    // The tab names the kit, not "Kit": outside signals, so an effect (ADR 0051).
    effect(() => {
      const template = this.store.template();
      if (template) titles.name(template.name);
    });
  }

  protected imageUrl(product: SearchHit): string | null {
    return product.imageId ? this.images.of(product.imageId) : null;
  }

  /** What the kit says of a slot, in this page's words and this reader's money (`slotSentence`). */
  protected said(slot: KitSlotView, t: (key: string, params?: Record<string, unknown>) => string): string | null {
    return slotSentence(slot, t, (amount) => formatPrice(amount, this.culture.active()));
  }

  protected build(event: Event): void {
    event.preventDefault();
    void this.store.build();
  }
}
