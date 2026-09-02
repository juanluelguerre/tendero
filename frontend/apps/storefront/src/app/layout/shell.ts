import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * The storefront shell. It is NOT shared with the backoffice: one is roomy and
 * light and the other dense and dark, and unifying them would give a component
 * with a variant matrix. See docs/adr/0010.
 */
@Component({
  selector: 'storefront-shell',
  imports: [RouterOutlet, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <header class="shell__header">
        <div class="shell__brand">
          <!-- The icon, not the full lockup: the wordmark beside it already uses
               real Bricolage, and the text embedded in the SVG only has Inter to
               fall back on. Two different typefaces for the same word show. Empty
               alt and aria-hidden because the name is right next to it as text:
               reading it twice is a nuisance. -->
          <img
            class="shell__mark"
            src="brand/tendero-icon-shop.svg"
            alt=""
            aria-hidden="true"
            width="44"
            height="44"
          />
          <span class="shell__wordmark">{{ t('brand.name') }}</span>
          <span class="shell__area">{{ t('brand.area') }}</span>
          <span class="shell__tagline">{{ t('brand.tagline') }}</span>
        </div>
      </header>
      <hr class="awning" />
      <main class="shell__main"><router-outlet /></main>
    </ng-container>
  `,
  styles: `
    :host { display: block; max-width: var(--container-app); margin-inline: auto; padding: 0 var(--space-5); }
    .shell__header { display: flex; align-items: center; min-height: var(--header-h); }
    /* center, not baseline: an image has no typographic baseline, and with
       baseline the icon hung below the wordmark. */
    .shell__brand { display: flex; align-items: center; gap: var(--space-3); }
    .shell__mark { display: block; width: 44px; height: 44px; }
    .shell__wordmark { font-family: var(--font-display); font-weight: 800; font-size: var(--text-xl); color: var(--accent); }
    .shell__area { font-size: var(--text-2xs); color: var(--text-muted); letter-spacing: var(--tracking-wide); text-transform: uppercase; }
    .shell__tagline { font-size: var(--text-2xs); color: var(--text-muted); }
    .shell__main { padding-block: var(--space-6); }
  `,
})
export class Shell {}
