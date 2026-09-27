import { ChangeDetectionStrategy, Component, computed, effect, inject, Injector, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { AuthStore } from '@tendero/shared-auth';
import { Culture, CultureStore } from '@tendero/shared-i18n';
import { TranslocoDirective } from '@jsverse/transloco';
import { StaffUsage } from '@tendero/shared-telemetry';
import { ThemeStore } from '@tendero/shared-ui';
import { filter } from 'rxjs';
import { backofficeAccess } from '../access';
import { BackofficeUsage } from '../usage';
import { AccountMenu } from './account-menu';
import { Icon } from './icon';
import { Nav } from './nav';
import { StaffNotice } from './staff-notice';
import { UndoToast } from '../work/undo-toast';
import { Waiting } from './waiting';

/**
 * The backoffice shell, as the redesign's R-3 drew it: the screens grouped by
 * job in a rail (a drawer on a phone), a one-row bar with the account menu, and
 * a content area that uses the width it has (R-1, B-F1, B-F9, B-F11). Dense,
 * and light or dark as the person chooses. None of this is shared with the
 * storefront (docs/adr/0010) except the theme's behaviour.
 *
 * The bar still names the role. With three seeded identities and three roles,
 * "why can I not publish?" is a question the interface should answer before it
 * is asked.
 */
@Component({
  selector: 'backoffice-shell',
  imports: [RouterOutlet, RouterLink, TranslocoDirective, StaffNotice, Nav, AccountMenu, Icon, UndoToast],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
  styleUrl: './shell.css',
  host: { '[class.with-rail]': 'canUseBackoffice()' },
})
export class Shell {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly cultureStore = inject(CultureStore);
  private readonly usage = inject(BackofficeUsage);
  private readonly injector = inject(Injector);

  /** The notice to staff and its switch (ADR 0041). */
  protected readonly staffUsage = inject(StaffUsage);

  protected readonly cultures = this.cultureStore.available;
  protected readonly culture = this.cultureStore.active;

  protected use(culture: Culture): void {
    this.cultureStore.use(culture);
  }

  protected readonly signedIn = this.auth.isSignedIn;
  protected readonly identity = this.auth.identity;

  /** The navigation is the shopkeeper's, the same answer the route guard gives:
      a rail offering screens to somebody every one of them turns away is the
      door the guard was fixed to close. */
  protected readonly canUseBackoffice = computed(() => backofficeAccess(this.identity()) === 'allowed');

  constructor() {
    // Created with the shell, so the page wears the chosen theme from the start
    // and follows the system while it changes.
    inject(ThemeStore);

    // R-0's measurement: every screen the shopkeeper opens, by its route (ADR 0041).
    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe((event) => {
        if (!this.canUseBackoffice()) return;
        this.usage.opened(event.urlAfterRedirects);
        // The badges' counts usually move with a navigation. Resolved here, and
        // only for somebody allowed in, so a turned-away role never asks.
        this.injector.get(Waiting).refresh();
      });

    // The token can stop being valid without anybody pressing anything: the
    // development issuer mints its signing key per process, so every restart
    // invalidates every token in a browser. The interceptor clears it on the
    // first 401; this is what turns that into a screen instead of a page of
    // failed panels.
    effect(() => {
      if (this.signedIn()) return;

      // The reason travels in the URL rather than in a signal, so it survives
      // the reload somebody presses when a screen surprises them. An arrival
      // with no reason is somebody who simply has not signed in yet, and
      // telling them their session expired would be inventing a session.
      const expired = this.auth.expired();

      untracked(() => {
        const from = this.router.url;
        if (from.startsWith('/sign-in')) return;

        // Where they were, in the same parameter the guard uses. A session that
        // ends under somebody mid-task and returns them to the home screen makes
        // them navigate back to work they had already found — and the two ways
        // of reaching this door should not differ in that.
        void this.router.navigate(['/sign-in'], {
          queryParams: expired ? { reason: 'expired', returnTo: from } : { returnTo: from },
        });
      });
    });
  }

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/sign-in']);
  }
}
