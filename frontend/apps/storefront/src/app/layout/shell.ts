import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Culture, CultureStore } from '@tendero/shared-i18n';
import { CartStore } from '../data-access/cart.service';

/**
 * The storefront shell. It is NOT shared with the backoffice: one is roomy and
 * light and the other dense and dark, and unifying them would give a component
 * with a variant matrix. See docs/adr/0010.
 *
 * The header sticks. On a results page the search box is the thing people come
 * back to, and making them scroll up to reach it is the single most common way a
 * shop wastes a visit.
 */
@Component({
  selector: 'storefront-shell',
  imports: [RouterOutlet, RouterLink, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <a class="skip" href="#main">{{ t('nav.skipToContent') }}</a>

      <header class="header">
        <div class="page-frame header__inner">
          <a class="brand" href="/">
            <!-- The icon, not the full lockup: the wordmark beside it already
                 uses real Bricolage, and the text embedded in the SVG only has
                 Inter to fall back on. Two different typefaces for the same word
                 show. Empty alt and aria-hidden because the name is right next
                 to it as text: reading it twice is a nuisance. -->
            <img
              class="brand__mark"
              src="brand/tendero-icon-shop.svg"
              alt=""
              aria-hidden="true"
              width="40"
              height="40"
            />
            <span class="brand__text">
              <span class="brand__wordmark">{{ t('brand.name') }}</span>
              <span class="brand__tagline">{{ t('brand.tagline') }}</span>
            </span>
          </a>
          <div class="header__end">
            <!-- The switcher is a pair of buttons and not a <select>: with two
                 options a dropdown hides half the answer behind a click, and
                 the current language should be readable without opening
                 anything. -->
            <div class="langs" role="group" [attr.aria-label]="t('nav.language')">
              @for (option of cultures; track option) {
                <button
                  type="button"
                  class="lang"
                  [class.lang--active]="option === culture()"
                  [attr.aria-pressed]="option === culture()"
                  (click)="use(option)"
                >
                  {{ option }}
                </button>
              }
            </div>
            <span class="brand__area eyebrow">{{ t('brand.area') }}</span>

            <!-- The basket. The count is on the badge and also in the label,
                 because a number in a circle is not something a screen reader
                 can make sense of on its own. -->
            <a class="cart" routerLink="/cart"
               [attr.aria-label]="t('nav.cart') + ' (' + cart.itemCount() + ')'">
              <svg class="cart__icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                <path
                  d="M3 4h2.2l2.3 10.5a2 2 0 0 0 2 1.5h7.6a2 2 0 0 0 2-1.6L20.5 7H6"
                  fill="none" stroke="currentColor" stroke-width="1.6"
                  stroke-linecap="round" stroke-linejoin="round" />
                <circle cx="10" cy="19.5" r="1.4" fill="currentColor" />
                <circle cx="17" cy="19.5" r="1.4" fill="currentColor" />
              </svg>
              @if (cart.itemCount() > 0) {
                <span class="cart__badge numeric">{{ cart.itemCount() }}</span>
              }
            </a>
          </div>
        </div>
        <!-- The signature, and the only decoration in the system. It marks where
             the chrome ends and the shop begins. -->
        <hr class="awning" />
      </header>

      <main id="main" class="page-frame main"><router-outlet /></main>

      <footer class="footer">
        <div class="page-frame footer__inner">
          <span class="muted">{{ t('brand.name') }} · {{ t('brand.tagline') }}</span>
        </div>
      </footer>
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-height: 100dvh;
    }

    /* Hidden until focused: the first thing a keyboard user meets should be a
       way past the chrome, and the first thing everyone else meets should not
       be a stray link. */
    .skip {
      position: absolute;
      inset-inline-start: var(--space-4);
      inset-block-start: calc(var(--space-4) * -4);
      z-index: 20;
      padding: var(--space-2) var(--space-4);
      background: var(--accent);
      color: var(--stone-0);
      border-radius: var(--radius-md);
      text-decoration: none;
      transition: inset-block-start var(--dur-fast) var(--ease);
    }
    .skip:focus-visible { inset-block-start: var(--space-4); }

    .header {
      position: sticky;
      inset-block-start: 0;
      z-index: 10;
      background: var(--bg-page);
    }

    .header__inner {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-4);
      min-height: var(--header-h);
    }

    .brand {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      text-decoration: none;
      color: inherit;
    }
    .brand__mark { width: 40px; height: 40px; }
    .brand__text { display: flex; flex-direction: column; line-height: var(--leading-tight); }
    .brand__wordmark {
      font-family: var(--font-display);
      font-weight: 800;
      font-size: var(--text-lg);
      color: var(--accent);
    }
    .brand__tagline { font-size: var(--text-2xs); color: var(--text-muted); }

    .header__end { display: flex; align-items: center; gap: var(--space-4); }

    /* The basket. It is NOT clay: one accent action per view, and on a results
       page that is the search button. The badge is, because it is the one thing
       here that changes and wants to be noticed when it does. */
    .cart {
      position: relative;
      display: grid;
      place-items: center;
      width: 44px;
      height: 44px;
      border-radius: var(--radius-md);
      color: var(--text);
      text-decoration: none;
      transition: background var(--dur-fast) var(--ease);
    }

    .cart:hover { background: var(--bg-raised); }
    .cart__icon { width: 22px; height: 22px; }

    .cart__badge {
      position: absolute;
      inset-block-start: 2px;
      inset-inline-end: 0;
      min-width: 18px;
      height: 18px;
      padding-inline: 4px;
      display: grid;
      place-items: center;
      border-radius: 9px;
      background: var(--accent);
      color: var(--stone-0);
      font-size: 11px;
      font-weight: 700;
      line-height: 1;
    }

    .langs {
      display: flex;
      border: 1px solid var(--border);
      border-radius: var(--radius-full);
      overflow: hidden;
      background: var(--bg-surface);
    }
    .lang {
      padding: var(--space-1) var(--space-3);
      border: 0;
      background: none;
      color: var(--text-muted);
      font: inherit;
      font-size: var(--text-2xs);
      text-transform: uppercase;
      letter-spacing: var(--tracking-wide);
      cursor: pointer;
    }
    .lang:hover { color: var(--text); }
    .lang--active { background: var(--accent); color: var(--stone-0); }

    .main { flex: 1; padding-block: var(--space-6) var(--space-8); }

    .footer { border-block-start: 1px solid var(--border); }
    .footer__inner { padding-block: var(--space-5); font-size: var(--text-2xs); }

    /* The tagline already says it under the wordmark; the area label is chrome
       and the first thing a narrow screen can afford to lose. */
    @media (max-width: 640px) {
      .brand__area { display: none; }
      .main { padding-block: var(--space-5) var(--space-6); }
    }
  `,
})
export class Shell {
  private readonly cultureStore = inject(CultureStore);

  protected readonly cultures = this.cultureStore.available;
  protected readonly culture = this.cultureStore.active;

  /**
   * Read once, here, so the badge is right on a cold load. Every page that
   * changes the cart updates the same store, so nothing else has to remember to
   * refresh it.
   */
  protected readonly cart = inject(CartStore);

  constructor() {
    void this.cart.load();
  }

  protected use(culture: Culture): void {
    this.cultureStore.use(culture);
  }
}
