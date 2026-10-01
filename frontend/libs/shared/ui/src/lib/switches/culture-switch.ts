import { booleanAttribute, ChangeDetectionStrategy, Component, input, output } from '@angular/core';

let switches = 0;

/**
 * The language, as both apps' account menus offer it: one segmented control,
 * each choice a button that says whether it is pressed. What choosing does is
 * the app's — the storefront navigates, because its URL carries the culture;
 * the backoffice switches in place — so this only says which was chosen. The
 * words are the caller's, as in the rest of this library.
 */
@Component({
  selector: 'lib-culture-switch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './culture-switch.html',
  host: { class: 'block' },
})
export class CultureSwitch<TCulture extends string = string> {
  readonly label = input.required<string>();
  /** False where the place around it already says what it is: then the label is only the group's name. */
  readonly labelVisible = input(true, { transform: booleanAttribute });
  readonly cultures = input.required<readonly TCulture[]>();
  readonly active = input.required<TCulture>();
  readonly chosen = output<TCulture>();

  protected readonly id = `culture-switch-${++switches}`;
}
