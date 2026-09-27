import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Telemetry } from '@tendero/shared-telemetry';
import { type ThemeChoice, ThemeChoices, ThemeStore } from '@tendero/shared-ui';

/**
 * Light, dark or as the system, chosen by the shopper (the redesign's R-5.1):
 * the same behaviour the backoffice has, from `shared-ui`, in the storefront's
 * own clothes (ADR 0010). It sits at the foot of the page and of the phone's
 * drawer, not in the header R-3 emptied.
 */
@Component({
  selector: 'storefront-theme-switch',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './theme-switch.html',
  styleUrl: './theme-switch.css',
})
export class ThemeSwitch {
  private readonly theme = inject(ThemeStore);
  private readonly telemetry = inject(Telemetry);

  protected readonly choices = ThemeChoices;
  protected readonly chosen = this.theme.choice;

  protected choose(choice: ThemeChoice): void {
    if (choice === this.chosen()) return;
    this.theme.choose(choice);
    this.telemetry.trackEvent('theme_chosen', { theme: choice });
  }
}
