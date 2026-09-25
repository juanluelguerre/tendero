import type { UrlTree } from '@angular/router';
import type { TrackedEvents } from '@tendero/shared-telemetry';

export type ArrivedFrom = TrackedEvents['product_viewed']['arrived_from'];

/**
 * Which kind of page led to a product, from the URL of the page before it
 * (`product_viewed`, the tracking plan). The kind and never the URL: a search's
 * words and a category's code say what somebody was looking for, and neither
 * is needed to answer where discovery works.
 *
 * `/:lang?q=…` is a search, `/:lang` the home page, `/:lang/c/:code` a
 * category; nothing before it in this tab is `direct` — a link from outside, a
 * bookmark, a new tab.
 */
export function arrivedFrom(previous: UrlTree | null | undefined): ArrivedFrom {
  if (!previous) return 'direct';

  const [, ...path] = previous.root.children['primary']?.segments.map((segment) => segment.path) ?? [];

  if (path.length === 0) return previous.queryParamMap.get('q') ? 'search' : 'home';
  if (path[0] === 'c') return 'category';
  return 'other';
}
