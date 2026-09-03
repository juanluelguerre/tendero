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
  templateUrl: './shell.html',
  styleUrl: './shell.css',
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
