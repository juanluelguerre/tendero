import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProductImagePlaceholder } from '../../product-image-placeholder';
import { map } from 'rxjs';
import { CultureStore, DEFAULT_CULTURE } from '@tendero/shared-i18n';
import type { ProductDetail, VariantView } from '@tendero/shared-api';
import { formatPrice, ProductImageUrls } from '@tendero/shared-util';
import { CartStore } from '../../data-access/cart.service';
import { ProductService } from '../../data-access/product.service';
import { CanonicalLinks } from '../../seo/canonical-links';
import { ShopLinks } from '../../shop-links';
import {
  availabilityOf,
  initialSelection,
  select,
  sellable,
  variantFor,
  type OptionAvailability,
  type Selection,
} from './variant-picker';

/**
 * The HTTP status behind a resource error. The response is either the error
 * itself or, when the runtime wraps a non-`Error` value, its `cause`.
 */
function statusOf(error: unknown): number | undefined {
  const failure = error as { status?: number; cause?: { status?: number } } | undefined;
  return failure?.status ?? failure?.cause?.status;
}

type PageState =
  | { status: 'loading' }
  | { status: 'done'; product: ProductDetail }
  | { status: 'notFound' }
  | { status: 'failed' };

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
 */
@Component({
  selector: 'storefront-product-page',
  imports: [TranslocoDirective, RouterLink, ProductImagePlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-page.html',
  styleUrl: './product-page.css',
})
export class ProductPage {
  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  private readonly products = inject(ProductService);
  private readonly cart = inject(CartStore);
  private readonly culture = inject(CultureStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly seo = inject(CanonicalLinks);
  private readonly images = inject(ProductImageUrls);

  /** Which option is chosen on each axis. */
  protected readonly selection = signal<Selection>({});

  protected readonly adding = signal(false);
  protected readonly broken = signal<ReadonlySet<string>>(new Set());

  /** Which image the gallery is showing, by id. Null means the cover. */
  protected readonly shown = signal<string | null>(null);

  private readonly code = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('code') ?? '')),
    { initialValue: '' },
  );

  private readonly slugFromUrl = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('slug') ?? '')),
    { initialValue: '' },
  );

  /**
   * The product, as a function of the page's only two inputs: the code in the
   * URL and the language.
   *
   * Reloading on a language change is not optional: the name, the description,
   * the attribute values and the variant labels are all resolved by the server
   * in the requested culture, so a switch that only retranslated the chrome
   * would leave Spanish product copy under English labels.
   *
   * It is a resource and not a subscribe inside an effect because a resource
   * CANCELS the request in flight when its inputs change. The hand-written
   * version did not, so switching language while a slow answer was on its way
   * could paint the old language over the new one — the answer that arrived
   * last won, whichever was asked for last.
   */
  private readonly detail = rxResource({
    params: () => {
      const code = this.code();
      return code ? { code, culture: this.culture.active() } : undefined;
    },
    stream: ({ params }) => this.products.get(params.code, params.culture),
  });

  protected readonly state = computed<PageState>(() => {
    if (!this.code()) return { status: 'notFound' };

    switch (this.detail.status()) {
      case 'resolved':
      case 'local':
        return { status: 'done', product: this.detail.value() as ProductDetail };
      case 'error':
        return { status: statusOf(this.detail.error()) === 404 ? 'notFound' : 'failed' };
      default:
        return { status: 'loading' };
    }
  });

  constructor() {
    // What the page does with an answer, once per answer: pick the initial
    // variant, put the address bar on the canonical URL and declare it to
    // crawlers. Kept out of the resource so that reading the product never has
    // side effects, and out of the template because navigation is not rendering.
    effect(() => {
      const current = this.state();

      untracked(() => {
        if (current.status === 'done') {
          this.selection.set(initialSelection(current.product));
          this.shown.set(null);
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
    if (this.slugFromUrl() === product.slug) return;

    void this.router.navigate(this.links.product(product.slug, product.code), {
      replaceUrl: true,
    });
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
      product.alternates.find((alternate) => alternate.culture === DEFAULT_CULTURE) ??
      product.alternates[0];

    this.seo.set(
      canonical,
      alternates,
      fallback ? this.links.productIn(DEFAULT_CULTURE, fallback.slug, product.code) : canonical,
    );
  }

  protected readonly product = computed(() => {
    const current = this.state();
    return current.status === 'done' ? current.product : null;
  });

  /** The variants a shopper can choose between — discontinued ones are not on sale. */
  protected readonly variants = computed(() => {
    const product = this.product();
    return product ? sellable(product) : [];
  });

  /** The one the picker is currently on, or null when the combination is not sold. */
  protected readonly chosen = computed(() => variantFor(this.variants(), this.selection()));

  protected availability(axis: string, option: string): OptionAvailability {
    return availabilityOf(this.variants(), this.selection(), axis, option);
  }

  protected choose(axis: string, option: string): void {
    this.selection.update((current) => select(this.variants(), current, axis, option));

    // A colour has its own photo; a size does not. Moving the gallery back to
    // the variant's image is what makes the picker feel connected to it.
    this.shown.set(this.chosen()?.imageId ?? null);
  }

  protected isChosen(axis: string, option: string): boolean {
    return this.selection()[axis] === option;
  }

  /**
   * What the page charges for. A single price when the picker has resolved to a
   * variant, and the product's range before it has — which is the same thing the
   * search card shows, so moving from one to the other never contradicts itself.
   */
  protected price(): string {
    const product = this.product();
    if (!product) return '';

    const culture = this.culture.active();
    const variant = this.chosen();

    if (variant) return formatPrice(variant.priceAmount, variant.priceCurrency, culture);

    if (product.priceFrom < product.priceTo) {
      return `${formatPrice(product.priceFrom, product.priceCurrency, culture)} – ${formatPrice(
        product.priceTo,
        product.priceCurrency,
        culture,
      )}`;
    }

    return formatPrice(product.priceFrom, product.priceCurrency, culture);
  }

  /** The gallery's images, cover first, with the chosen variant's promoted. */
  protected readonly gallery = computed(() => {
    const product = this.product();
    if (!product) return [];

    const shown = this.shown();
    const images = [...product.images];

    if (!shown) return images;

    const promoted = images.filter((image) => image.imageId === shown);
    return promoted.length > 0
      ? [...promoted, ...images.filter((image) => image.imageId !== shown)]
      : images;
  });

  protected imageUrl(imageId: string): string {
    return this.images.of(imageId);
  }

  protected onImageError(imageId: string): void {
    this.broken.update((ids) => new Set(ids).add(imageId));
  }

  /**
   * Adds the chosen variant. The button is disabled without one, so a shopper
   * cannot buy a combination the shop does not sell — which is the whole reason
   * the picker distinguishes "not sold" from "sold out".
   */
  protected async addToCart(): Promise<void> {
    const variant = this.chosen();
    if (!variant || !variant.inStock) return;

    this.adding.set(true);

    try {
      await this.cart.add(variant.sku);
    } finally {
      this.adding.set(false);
    }
  }

  /** Whether the shelf is thin enough to be worth saying. The API only sends a
   *  number when it is, so the page never decides the threshold. */
  protected remaining(): number | null {
    return this.chosen()?.remaining ?? null;
  }

  protected variantOf(sku: string): VariantView | undefined {
    return this.variants().find((variant) => variant.sku === sku);
  }
}
