import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Icon } from '../icon/icon';
import { type ThemeChoice, ThemeChoices } from '../theme';

let switches = 0;

/**
 * The theme, as both apps' account menus offer it: system, light or dark, each
 * an icon with its name as the button's, in the same segmented control as the
 * language. Choosing is the app's (it applies it through the ThemeStore and
 * counts it in its own telemetry); this only says which was chosen.
 */
@Component({
  selector: 'lib-theme-switch',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './theme-switch.html',
  styleUrl: './theme-switch.css',
})
export class ThemeSwitch {
  readonly label = input.required<string>();
  /** Each choice's name, in the reader's language: the button's accessible name and its tooltip. */
  readonly names = input.required<Record<ThemeChoice, string>>();
  readonly choice = input.required<ThemeChoice>();
  readonly chosen = output<ThemeChoice>();

  protected readonly choices = ThemeChoices;
  protected readonly id = `theme-switch-${++switches}`;
}
