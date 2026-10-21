import { Component, input, output } from '@angular/core';

/**
 * A yes-or-no fact as a switch: a button that says it is one (`role="switch"`,
 * `aria-checked`), its label inside it so the whole row is the target, and a track
 * and knob drawn from the tokens (`--spacing-switch-*`, `--spacing-knob-*`), clay
 * when on.
 *
 * One control for every page that has a switch. It was the filter panel's alone,
 * drawn in its template; the trail's was a browser checkbox at 24 px, the kit's
 * requirements and the account's preferences browser checkboxes at their default
 * 13 px, under the 24 px floor the shop holds every target to — four looks for one
 * idea, found by the author on the preferences page (2026-10-21).
 *
 * It says what was asked, never what is: pressing it emits the other value, and
 * the page decides and binds `checked` back. A switch that flipped itself could
 * say *on* for a consent the server refused.
 */
@Component({
  selector: 'lib-toggle-switch',
  templateUrl: './toggle-switch.html',
  host: { class: 'block' },
})
export class ToggleSwitch {
  readonly checked = input.required<boolean>();
  readonly disabled = input(false);

  /** The value the person asked for: the opposite of `checked` when it was pressed. */
  readonly toggled = output<boolean>();

  protected press(): void {
    if (!this.disabled()) this.toggled.emit(!this.checked());
  }
}
