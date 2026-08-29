import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * Shell del backoffice: denso y oscuro. Nada de esto se comparte con el
 * storefront (docs/adr/0010) — la raya de toldo marca la pestana activa, que es
 * uno de los tres sitios donde el sistema permite decoracion.
 */
@Component({
  selector: 'backoffice-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <header class="bar">
        <span class="bar__brand">{{ t('brand.name') }}</span>
        <span class="bar__area">{{ t('brand.area') }}</span>
      </header>
      <nav class="tabs" [attr.aria-label]="t('brand.area')">
        <a class="tab" routerLink="/review" routerLinkActive="tab--active">
          {{ t('nav.reviewQueue') }}<hr class="awning awning-thin tab__mark" />
        </a>
      </nav>
      <main class="main"><router-outlet /></main>
    </ng-container>
  `,
  styles: `
    :host { display: block; min-height: 100dvh; background: var(--bg-page); color: var(--text); font-size: var(--text-sm); }
    .bar { display: flex; align-items: baseline; gap: var(--space-2); height: 44px; padding-inline: var(--space-4); border-block-end: 1px solid var(--border); }
    .bar__brand { font-family: var(--font-display); font-weight: 800; color: var(--accent); }
    .bar__area { font-size: var(--text-2xs); color: var(--text-muted); letter-spacing: var(--tracking-wide); text-transform: uppercase; }
    .tabs { display: flex; gap: var(--space-4); padding-inline: var(--space-4); border-block-end: 1px solid var(--border); }
    .tab { position: relative; padding-block: var(--space-3); font-size: var(--text-xs); color: var(--text-muted); text-decoration: none; }
    .tab:focus-visible { outline: 2px solid var(--focus-ring); outline-offset: 2px; }
    .tab__mark { position: absolute; inset-inline: 0; inset-block-end: 0; margin: 0; opacity: 0; }
    .tab--active { color: var(--text); }
    .tab--active .tab__mark { opacity: 1; }
    .main { padding: var(--space-5) var(--space-4); }
  `,
})
export class Shell {}
