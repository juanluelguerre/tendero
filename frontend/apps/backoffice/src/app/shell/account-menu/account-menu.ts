import { Component, computed, inject, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthStore } from '@tendero/shared-auth';
import { type Culture, CultureStore } from '@tendero/shared-i18n';
import { CultureSwitch, Icon, type ThemeChoice, ThemeStore, ThemeSwitch } from '@tendero/shared-ui';
import { BackofficeUsage } from '../../usage';

/**
 * Who is signed in and what they can set for themselves: the language, the
 * theme and signing out (R-3). They moved here from the header so a phone's bar
 * holds one row (R-1, B-F9). The role stays on the trigger itself, because "why
 * can I not publish?" should be answered before it is asked.
 *
 * A native popover: the platform gives it the light dismiss, `Esc` and the top
 * layer, and none of that is ours to maintain.
 */
@Component({
  selector: 'backoffice-account-menu',
  imports: [TranslocoDirective, Icon, CultureSwitch, ThemeSwitch],
  templateUrl: './account-menu.html',
  styleUrl: './account-menu.css',
  host: { class: 'inline-flex' },
})
export class AccountMenu {
  private readonly auth = inject(AuthStore);
  private readonly cultureStore = inject(CultureStore);
  private readonly theme = inject(ThemeStore);
  private readonly usage = inject(BackofficeUsage);

  readonly signOut = output<void>();

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

  protected use(culture: Culture): void {
    this.cultureStore.use(culture);
  }

  protected choose(choice: ThemeChoice): void {
    if (choice === this.themeChoice()) return;
    this.theme.choose(choice);
    this.usage.themeChosen(choice);
  }
}
