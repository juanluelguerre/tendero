import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthStore } from '@tendero/shared-auth';
import { Culture, CultureStore } from '@tendero/shared-i18n';
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

          <!-- Same control as the storefront's, styled for a dense dark bar:
               ADR 0010 shares the behaviour and not the component. -->
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

    /* Pushed to the right by margin-inline-start:auto, so the language, the
       identity and sign-out read as one group at the end of the bar. */
    .langs {
      margin-inline-start: auto;
      display: flex;
      border: 1px solid var(--border-strong);
      border-radius: var(--radius-md);
      overflow: hidden;
    }
    .lang {
      padding: 2px var(--space-2);
      border: 0;
      background: none;
      color: var(--text-muted);
      font: inherit;
      font-size: var(--text-3xs);
      text-transform: uppercase;
      letter-spacing: var(--tracking-wide);
      cursor: pointer;
    }
    .lang:hover { color: var(--text); }
    .lang--active { background: var(--accent); color: var(--ink-900); }

    /* Who is acting, and under which role. */
    .who {
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
  private readonly cultureStore = inject(CultureStore);

  protected readonly cultures = this.cultureStore.available;
  protected readonly culture = this.cultureStore.active;

  protected use(culture: Culture): void {
    this.cultureStore.use(culture);
  }

  protected readonly signedIn = this.auth.isSignedIn;
  protected readonly identity = this.auth.identity;

  constructor() {
    // The token can stop being valid without anybody pressing anything: the
    // development issuer mints its signing key per process, so every restart
    // invalidates every token in a browser. The interceptor clears it on the
    // first 401; this is what turns that into a screen instead of a page of
    // failed panels.
    effect(() => {
      if (this.signedIn()) return;

      untracked(() => {
        if (!this.router.url.startsWith('/sign-in')) void this.router.navigate(['/sign-in']);
      });
    });
  }

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/sign-in']);
  }
}
