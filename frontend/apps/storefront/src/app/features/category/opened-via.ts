import type { Navigation } from '@angular/router';
import type { TrackedEvents } from '@tendero/shared-telemetry';
import { arrivedFrom } from '../product/arrived-from';

export type OpenedVia = TrackedEvents['category_opened']['arrived_from'];

/**
 * How a shopper reached a category listing (`category_opened`, the tracking plan;
 * R-0 of the redesign). A link from the category menu says so in its navigation
 * state, `{ via: 'menu' }`, once the storefront has one (R-5); anything else is the
 * kind of page before this one, the same reading `product_viewed` makes. Before
 * the menu exists, `menu` never appears — which is the baseline.
 */
export function openedVia(navigation: Navigation | null | undefined): OpenedVia {
  if (navigation?.extras.state?.['via'] === 'menu') return 'menu';
  return arrivedFrom(navigation?.previousNavigation?.finalUrl);
}
