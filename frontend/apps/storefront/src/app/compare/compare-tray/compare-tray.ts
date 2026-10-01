import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CompareStore } from '../compare.store';
import { ShopLinks } from '../../shop-links';

/**
 * The products picked for comparison, at the foot of every page until they are
 * compared or dropped (P10-7). It appears with the first pick and asks for a
 * second before it offers the comparison: one product compared with nothing is
 * its own page.
 */
@Component({
  selector: 'storefront-compare-tray',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './compare-tray.html',
  // The host is what sticks: a sticky element cannot leave its
  // parent, and the component's own element is that parent — inline by default, so the
  // tray stuck to nothing until the host became the sticky box.
  host: { class: 'block sticky bottom-0 z-10' },
})
export class CompareTray {
  protected readonly compare = inject(CompareStore);
  protected readonly links = inject(ShopLinks);

  protected readonly query = computed(() => ({ codes: this.compare.codes().join(',') }));
}
