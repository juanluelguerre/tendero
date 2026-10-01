import { afterRenderEffect, ChangeDetectionStrategy, Component, type ElementRef, input, output, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Icon } from '@tendero/shared-ui';

let panes = 0;

/**
 * The pane beside a list of work (the redesign's R-3, P6): open an item, act,
 * go on down the list without losing your place. Beside the list from 1024 px,
 * a sheet over it below, with Back.
 *
 * It owns what every pane must do the same way: the heading takes the focus
 * whenever it names a different item, so a keyboard and a screen reader are
 * where the eye went; `Esc` closes it unless a dialog of its own is open; and
 * it is a named landmark. What goes inside is the screen's.
 */
@Component({
  selector: 'backoffice-worklist-pane',
  imports: [TranslocoDirective, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './worklist-pane.html',
  styleUrl: './worklist-pane.css',
  host: {
    role: 'complementary',
    '[attr.aria-labelledby]': 'headingId',
    '(keydown.escape)': 'escape()',
  },
})
export class WorklistPane {
  readonly heading = input.required<string>();
  readonly closed = output<void>();

  protected readonly headingId = `worklist-pane-${++panes}`;
  private readonly title = viewChild.required<ElementRef<HTMLElement>>('title');

  constructor() {
    afterRenderEffect(() => {
      this.heading();
      this.title().nativeElement.focus();
    });
  }

  protected escape(): void {
    // A confirmation open inside the pane gets the Esc, not the pane.
    if (this.title().nativeElement.ownerDocument.querySelector('dialog[open]')) return;
    this.closed.emit();
  }
}
