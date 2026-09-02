import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthStore } from '@tendero/shared-auth';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * The backoffice shell: dense and dark. None of this is shared with the
 * storefront (docs/adr/0010) — the awning stripe marks the active tab, which is
 * one of the three places the system allows decoration.
 *
 * The bar names who is signed in. With three seeded identities and three roles,
 * "why can I not publish?" is a question the interface should answer before it
 * is asked.
 */
@Component({
  selector: 'backoffice-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <header class="bar">
        <div class="page-frame bar__inner">
          <!-- See the storefront shell: the icon, not the lockup, because of the
               typeface. It is 26 px here because the bar is dense by design. -->
          <img
            class="bar__mark"
            src="brand/tendero-icon-backoffice.svg"
            alt=""
            aria-hidden="true"
            width="26"
            height="26"
          />
          <span class="bar__brand">{{ t('brand.name') }}</span>
          <span class="bar__area eyebrow">{{ t('brand.area') }}</span>

          @if (identity(); as who) {
            <div class="who">
              <span class="who__name">{{ who.name }}</span>
              <span class="tag tag--code">{{ who.role }}</span>
            </div>
            <!-- Without this you cannot switch identity, and trying the roles
                 means clearing localStorage by hand. -->
            <button class="button button--quiet" type="button" (click)="signOut()">
              {{ t('signIn.signOut') }}
            </button>
          }
        </div>
      </header>

      @if (signedIn()) {
        <nav class="tabs" [attr.aria-label]="t('brand.area')">
          <div class="page-frame tabs__inner">
            <a class="tab" routerLink="/review" routerLinkActive="tab--active">
              {{ t('nav.reviewQueue') }}<hr class="awning awning-thin tab__mark" />
            </a>
            <a class="tab" routerLink="/attributes" routerLinkActive="tab--active">
              {{ t('nav.attributes') }}<hr class="awning awning-thin tab__mark" />
            </a>
            <a class="tab" routerLink="/promotions" routerLinkActive="tab--active">
              {{ t('nav.promotions') }}<hr class="awning awning-thin tab__mark" />
            </a>
          </div>
        </nav>
      }

      <main class="page-frame main"><router-outlet /></main>
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-height: 100dvh;
      background: var(--bg-page);
      color: var(--text);
    }

    .bar { border-block-end: 1px solid var(--border); background: var(--bg-surface); }
    .bar__inner {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      min-height: 48px;
    }
    .bar__mark { display: block; width: 26px; height: 26px; }
    .bar__brand { font-family: var(--font-display); font-weight: 800; color: var(--accent); }

    /* Who is acting, and under which role. It sits to the right of the brand and
       left of sign-out so the two read as one group. */
    .who {
      margin-inline-start: auto;
      display: flex;
      align-items: center;
      gap: var(--space-2);
      font-size: var(--text-xs);
    }
    .who__name { color: var(--text-muted); }

    .tabs { border-block-end: 1px solid var(--border); background: var(--bg-surface); }
    .tabs__inner { display: flex; gap: var(--space-5); }

    .tab {
      position: relative;
      padding-block: var(--space-3);
      font-size: var(--text-xs);
      color: var(--text-muted);
      text-decoration: none;
      transition: color var(--dur-fast) var(--ease);
    }
    .tab:hover { color: var(--text); }
    .tab__mark { position: absolute; inset-inline: 0; inset-block-end: -1px; margin: 0; opacity: 0; }
    .tab--active { color: var(--text); }
    .tab--active .tab__mark { opacity: 1; }

    .main { flex: 1; padding-block: var(--space-6) var(--space-8); }

    /* The name goes first on a narrow bar: the role tag still says which powers
       this identity has, and the brand is already in the icon. */
    @media (max-width: 640px) {
      .bar__area, .who__name { display: none; }
      .tabs__inner { gap: var(--space-4); overflow-x: auto; }
      .main { padding-block: var(--space-5); }
    }
  `,
})
export class Shell {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected readonly signedIn = this.auth.isSignedIn;
  protected readonly identity = this.auth.identity;

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/sign-in']);
  }
}
