import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthStore } from '@tendero/shared-auth';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * The backoffice shell: dense and dark. None of this is shared with the
 * storefront (docs/adr/0010) — the awning stripe marks the active tab, which is
 * one of the three places the system allows decoration.
 */
@Component({
  selector: 'backoffice-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <header class="bar">
        <!-- See the storefront shell: the icon, not the lockup, because of the
             typeface. It is 28 px here because the bar is 44 and dense by design. -->
        <img
          class="bar__mark"
          src="brand/tendero-icon-backoffice.svg"
          alt=""
          aria-hidden="true"
          width="28"
          height="28"
        />
        <span class="bar__brand">{{ t('brand.name') }}</span>
        <span class="bar__area">{{ t('brand.area') }}</span>
        <!-- Without this you cannot switch identity, and trying the roles means
             clearing localStorage by hand. -->
        @if (signedIn()) {
          <button class="bar__signout" type="button" (click)="signOut()">
            {{ t('signIn.signOut') }}
          </button>
        }
      </header>
      <nav class="tabs" [attr.aria-label]="t('brand.area')">
        <a class="tab" routerLink="/review" routerLinkActive="tab--active">
          {{ t('nav.reviewQueue') }}<hr class="awning awning-thin tab__mark" />
        </a>
        <a class="tab" routerLink="/attributes" routerLinkActive="tab--active">
          {{ t('nav.attributes') }}<hr class="awning awning-thin tab__mark" />
        </a>
        <a class="tab" routerLink="/promotions" routerLinkActive="tab--active">
          {{ t('nav.promotions') }}<hr class="awning awning-thin tab__mark" />
        </a>
      </nav>
      <main class="main"><router-outlet /></main>
    </ng-container>
  `,
  styles: `
    :host { display: block; min-height: 100dvh; background: var(--bg-page); color: var(--text); font-size: var(--text-sm); }
    .bar { display: flex; align-items: center; gap: var(--space-2); height: 44px; padding-inline: var(--space-4); border-block-end: 1px solid var(--border); }
    .bar__mark { display: block; width: 28px; height: 28px; }
    .bar__brand { font-family: var(--font-display); font-weight: 800; color: var(--accent); }
    .bar__area { font-size: var(--text-2xs); color: var(--text-muted); letter-spacing: var(--tracking-wide); text-transform: uppercase; }
    .bar__signout { margin-inline-start: auto; background: none; border: 0; color: var(--text-muted); font: inherit; font-size: var(--text-xs); cursor: pointer; }
    .bar__signout:hover { color: var(--text); }
    .bar__signout:focus-visible { outline: 2px solid var(--focus-ring); outline-offset: 2px; }
    .tabs { display: flex; gap: var(--space-4); padding-inline: var(--space-4); border-block-end: 1px solid var(--border); }
    .tab { position: relative; padding-block: var(--space-3); font-size: var(--text-xs); color: var(--text-muted); text-decoration: none; }
    .tab:focus-visible { outline: 2px solid var(--focus-ring); outline-offset: 2px; }
    .tab__mark { position: absolute; inset-inline: 0; inset-block-end: 0; margin: 0; opacity: 0; }
    .tab--active { color: var(--text); }
    .tab--active .tab__mark { opacity: 1; }
    .main { padding: var(--space-5) var(--space-4); }
  `,
})
export class Shell {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected readonly signedIn = this.auth.isSignedIn;

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/sign-in']);
  }
}
