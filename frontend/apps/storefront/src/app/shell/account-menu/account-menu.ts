import { Component, computed, type ElementRef, inject, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthStore } from '@tendero/shared-auth';
import { type Culture, CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { CultureSwitch, Icon, type ThemeChoice, ThemeStore, ThemeSwitch } from '@tendero/shared-ui';
import { ShopLinks } from '../../shop-links';

/**
 * Who is shopping and how the shop is shown to them: the language, the theme,
 * and the way into their account — the backoffice's account menu, in the
 * storefront's clothes (the author, 2026-09-28; ADR 0010). They were three
 * controls in the header and three more at the foot of the phone's drawer; now
 * they are one place at every width.
 *
 * A shopper who has not signed in is a guest, and is told so: the shop works
 * signed out, so the menu offers signing in and never demands it.
 *
 * A native popover: the platform gives it the light dismiss, `Esc` and the top
 * layer.
 */
@Component({
  selector: 'storefront-account-menu',
  imports: [TranslocoDirective, RouterLink, CultureSwitch, Icon, ThemeSwitch],
  templateUrl: './account-menu.html',
  styleUrl: './account-menu.css',
  host: { class: 'inline-flex' },
})
export class AccountMenu {
  private readonly auth = inject(AuthStore);
  private readonly cultureStore = inject(CultureStore);
  private readonly theme = inject(ThemeStore);
  private readonly telemetry = inject(Telemetry);
  private readonly router = inject(Router);
  private readonly links = inject(ShopLinks);

  private readonly menu = viewChild.required<ElementRef<HTMLElement>>('menu');

  protected readonly identity = this.auth.identity;
  protected readonly initials = computed(() =>
    (this.identity()?.name ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((word) => word[0].toUpperCase())
      .join(''),
  );

  protected readonly cultures = this.cultureStore.available;
  protected readonly culture = this.cultureStore.active;
  protected readonly themeChoice = this.theme.choice;
  protected readonly accountLink = computed(() => ['/', this.culture(), 'account']);

  /**
   * Switching language NAVIGATES; it does not just flip a signal. The URL is
   * what carries the culture (`P5-13`), so swapping the first segment is the
   * switch, and a language change is a page a person can bookmark and share.
   */
  protected use(culture: Culture): void {
    void this.router.navigateByUrl(this.links.sameIn(this.router.url, culture));
  }

  protected choose(choice: ThemeChoice): void {
    if (choice === this.themeChoice()) return;
    this.theme.choose(choice);
    this.telemetry.trackEvent('theme_chosen', { theme: choice });
  }

  /** A link inside a popover leaves it open over the new page; it closes on the way. */
  protected close(): void {
    this.menu().nativeElement.hidePopover();
  }

  protected signOut(): void {
    this.close();
    this.auth.signOut();
  }
}
