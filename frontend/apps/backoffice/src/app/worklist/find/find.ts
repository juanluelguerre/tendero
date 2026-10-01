import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * The search above a list of work (R-3, P3): a box that narrows the list as it
 * is typed in, named for a screen reader by a label nobody sees, because the
 * placeholder says what it searches and a placeholder is not a name.
 */
@Component({
  selector: 'backoffice-find',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './find.html',
  styleUrl: './find.css',
})
export class Find {
  readonly label = input.required<string>();
  readonly placeholder = input.required<string>();
  readonly value = input.required<string>();
  readonly searched = output<string>();
}
