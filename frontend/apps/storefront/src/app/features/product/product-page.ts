import { Component, computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore, DEFAULT_CULTURE, PageTitles } from '@tendero/shared-i18n';
import type { ProductDetail } from '@tendero/shared-api';
import { Telemetry } from '@tendero/shared-telemetry';
import { formatPrice, ProductImageUrls } from '@tendero/shared-util';
import { filter } from 'rxjs';
import { Crumbs } from '../../crumbs/crumbs';
import { ProductImage } from '../../product-image/product-image';
import { ReturnsPolicyStore } from '../../returns/returns-policy.store';
import { CanonicalLinks } from '../../seo/canonical-links';
import { ShopLinks } from '../../shop-links';
import { SpecSource } from '../../specs/spec-source/spec-source';
import { SpecValue } from '../../specs/spec-value/spec-value';
import { TrailStore } from '../../trail/trail.store';
import { arrivedVia, cameFiltered } from './arrived-from';
import { ProductStore } from './product.store';
import { PageState } from '../../page-state/page-state';

/**
 * The product detail page.
 *
 * It was three tasks' blocker for two phases. Phase 1 deferred the variant
 * picker to "the cart phase, where it is needed rather than decorative", and the
 * cart phase went from the search card straight to the basket — which works at
 * one variant per product and would not at eight.
 *
 * The URL is `/p/{slug}/{code}` (ADR 0026). The CODE is the key and the slug is
 * decoration, so a renamed product keeps working: the page compares the slug it
 * was given against the canonical one the API answers with, and quietly replaces
 * the URL when they differ. That is the client half of a 301 — the shopper never
 * sees a 404 and the address bar ends up on the right address.
 *
 * What the page shows is its store's (ADR 0047); the page presents it and
 * keeps the head — the tab's title, the canonical address, the view it reports.
 */
@Component({
  selector: 'storefront-product-page',
  imports: [TranslocoDirective, RouterLink, Crumbs, ProductImage, SpecValue, SpecSource, PageState],
  providers: [ProductStore],
  templateUrl: './product-page.html',
})
export class ProductPage {
  protected readonly store = inject(ProductStore);

  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  private readonly culture = inject(CultureStore);
  private readonly router = inject(Router);
  private readonly seo = inject(CanonicalLinks);
  private readonly titles = inject(PageTitles);
  private readonly images = inject(ProductImageUrls);
  private readonly telemetry = inject(Telemetry);
  private readonly trail = inject(TrailStore);

  /** The product last reported as viewed, so an answer in another language is not a second view. */
  private viewed: string | null = null;

  /**
   * The returns promise, and it is the SERVER's number.
   *
   * Roughly sixty per cent of shoppers look for the return policy on the product
   * page and forty-four per cent of shops do not put it there; this page was one
   * of them, while the machinery that honours it had shipped a phase earlier.
   *
   * It is null until the call answers, and stays null if it failed — the block
   * simply does not render. A default of fourteen typed here would be exactly
   * the copy the policy's store exists to remove.
   */
  protected readonly returnWindowDays = inject(ReturnsPolicyStore).windowDays;

  /** The branch's names, root first, for the crumbs. */
  protected readonly crumbs = computed(() => this.store.product()?.category.map((step) => step.name) ?? []);

  /** Every finished navigation, so the tab can be renamed after the router
      names it — see the first effect in the constructor. */
  private readonly navigated = toSignal(this.router.events.pipe(filter((event) => event instanceof NavigationEnd)));

  constructor() {
    // The tab says which product, not "Product". After EVERY finished
    // navigation and not once per answer, because the canonical correction
    // below is a navigation of its own: the router names the page from its
    // route when it ends, and a name set before that is overwritten. Found
    // live — the first version titled every product page "Producto".
    effect(() => {
      this.navigated();
      const product = this.store.product();
      if (product) untracked(() => this.titles.name(product.name));
    });

    // What the page does with an answer, once per answer: report the view, put
    // the address bar on the canonical URL and declare it to crawlers. Out of
    // the store, because reading the product never has side effects, and out
    // of the template because navigation is not rendering.
    effect(() => {
      const current = this.store.state();

      untracked(() => {
        if (current.status === 'done') {
          this.reportView(current.product.code);
          this.canonicalise(current.product);
          this.declare(current.product);
        } else if (current.status === 'failed' || current.status === 'notFound') {
          // A page that cannot be shown must not leave the previous product's
          // canonical tag behind it: that would tell a crawler an error page is
          // the real address of something we sell.
          this.seo.clear();
        }
      });
    });
  }

  /**
   * `product_viewed` (the tracking plan): once per product, with the kind of
   * page that led here — read BEFORE the canonical correction below, which is
   * a navigation of its own and would make this page its own predecessor. The
   * facade drops it unless the shopper allowed analytics.
   */
  private reportView(code: string): void {
    if (this.viewed === code) return;
    this.viewed = code;

    const navigation = this.router.lastSuccessfulNavigation();
    const previous = navigation?.previousNavigation?.finalUrl;
    this.telemetry.trackEvent('product_viewed', {
      arrived_from: arrivedVia(navigation),
      filtered: cameFiltered(previous),
    });

    // *Sigue donde lo dejaste* (R-8): once per product, as the event is. The
    // trail decides where it is kept, and whether it is kept at all.
    this.trail.recordView(code);
  }

  /**
   * Puts the address bar on the canonical URL without adding a history entry.
   *
   * It fires in two cases and the same line covers both: the product was renamed
   * since the link was made, and the link was to another language's slug. In
   * both, the code resolved and the slug did not match — which is exactly the
   * comparison the API's response was shaped to make possible.
   *
   * `replaceUrl` because this is a correction and not a navigation: pressing
   * back should leave the page, not walk through the wrong address.
   */
  private canonicalise(product: ProductDetail): void {
    if (this.store.slugFromUrl() === product.slug) return;
    void this.router.navigate(this.links.product(product.slug, product.code), { replaceUrl: true });
  }

  /**
   * The canonical URL and one alternate per language.
   *
   * The API answers with EVERY culture's slug, including the one being served,
   * which is what makes this a lookup rather than a guess — and the reason the
   * response was shaped that way. Building the English URL by slugifying the
   * Spanish name would produce a link to a page that does not exist.
   *
   * `x-default` is the shop's default language, which is the honest answer to
   * "somebody whose language we do not speak": send them to Spanish rather than
   * to whichever page the crawler saw first.
   */
  private declare(product: ProductDetail): void {
    const canonical = this.links.productIn(this.culture.active(), product.slug, product.code);
    const alternates = product.alternates.map((alternate) => ({
      culture: alternate.culture,
      path: this.links.productIn(alternate.culture, alternate.slug, product.code),
    }));
    const fallback =
      product.alternates.find((alternate) => alternate.culture === DEFAULT_CULTURE) ?? product.alternates[0];

    this.seo.set(
      canonical,
      alternates,
      fallback ? this.links.productIn(DEFAULT_CULTURE, fallback.slug, product.code) : canonical,
    );
  }

  /**
   * What the page charges for. A single price when the picker has resolved to a
   * variant, and the product's range before it has — which is the same thing the
   * search card shows, so moving from one to the other never contradicts itself.
   */
  protected price(): string {
    const product = this.store.product();
    if (!product) return '';

    const culture = this.culture.active();
    const variant = this.store.chosen();
    if (variant) return formatPrice(variant.priceAmount, variant.priceCurrency, culture);

    if (product.priceFrom < product.priceTo) {
      return `${formatPrice(product.priceFrom, product.priceCurrency, culture)} – ${formatPrice(product.priceTo, product.priceCurrency, culture)}`;
    }
    return formatPrice(product.priceFrom, product.priceCurrency, culture);
  }

  protected imageUrl(imageId: string): string {
    return this.images.of(imageId);
  }

  protected cartLink(): string[] {
    return ['/', this.culture.active(), 'cart'];
  }

  /** Whether the notice's link is used (`cart_notice_followed`, the tracking plan). */
  protected noticeFollowed(): void {
    this.telemetry.trackEvent('cart_notice_followed', {});
  }
}
