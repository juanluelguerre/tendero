import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * El shell del storefront. NO se comparte con el backoffice: uno es espacioso y
 * claro y el otro denso y oscuro, y unificarlos daria un componente con matriz
 * de variantes. Ver docs/adr/0010.
 */
@Component({
  selector: 'storefront-shell',
  imports: [RouterOutlet, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <header class="shell__header">
        <div class="shell__brand">
          <span class="shell__wordmark">{{ t('brand.name') }}</span>
          <span class="shell__tagline">{{ t('brand.tagline') }}</span>
        </div>
      </header>
      <hr class="awning" />
      <main class="shell__main"><router-outlet /></main>
    </ng-container>
  `,
  styles: `
    :host { display: block; max-width: var(--container-app); margin-inline: auto; padding: 0 var(--space-5); }
    .shell__header { display: flex; align-items: baseline; min-height: var(--header-h); }
    .shell__brand { display: flex; align-items: baseline; gap: var(--space-3); }
    .shell__wordmark { font-family: var(--font-display); font-weight: 800; font-size: var(--text-xl); color: var(--accent); }
    .shell__tagline { font-size: var(--text-2xs); color: var(--text-muted); }
    .shell__main { padding-block: var(--space-6); }
  `,
})
export class Shell {}
