import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Icon } from './icon';
import { Navigation } from './navigation';
import { Waiting } from './waiting';

/**
 * The screens, grouped by job (R-3, P1). One component draws both places they
 * appear: the rail beside the content on a tablet or wider, and the drawer on a
 * phone. The rail narrows to icons between 768 and 1199 px, with the label kept
 * as the link's accessible name and shown on hover or focus.
 */
@Component({
  selector: 'backoffice-nav',
  imports: [RouterLink, RouterLinkActive, TranslocoDirective, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './nav.html',
  styleUrl: './nav.css',
  host: { '[class.in-drawer]': 'inDrawer()' },
})
export class Nav {
  /** In the phone's drawer the labels always show, whatever the width. */
  readonly inDrawer = input(false);

  /** A link was followed: the drawer closes on it. */
  readonly chosen = output<void>();

  protected readonly groups = Navigation;
  protected readonly waiting = inject(Waiting).of;
}
