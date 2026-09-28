import type { UrlTree } from '@angular/router';
import type { TrackedEvents } from '@tendero/shared-telemetry';
import { hasFilters, parseListing } from '../../listing/listing-filters';

export type ArrivedFrom = TrackedEvents['product_viewed']['arrived_from'];

/**
 * Which kind of page led to a product, from the URL of the page before it
 * (`product_viewed`, the tracking plan). The kind and never the URL: a search's
 * words and a category's code say what somebody was looking for, and neither
 * is needed to answer where discovery works.
 *
 * `/:lang?q=…` is a search, `/:lang` the home page, `/:lang?offer=1` a
 * listing made only of filters (Ofertas, Novedades — F-6: counted as `home`
 * until then), `/:lang/c/:code` a category; nothing before it in this tab is
 * `direct` — a link from outside, a bookmark, a new tab.
 */
export function arrivedFrom(previous: UrlTree | null | undefined): ArrivedFrom {
  if (!previous) return 'direct';

  const [, ...path] = previous.root.children['primary']?.segments.map((segment) => segment.path) ?? [];

  if (path.length === 0) {
    if (previous.queryParamMap.get('q')) return 'search';
    return hasFilters(parseListing(previous.queryParamMap)) ? 'listing' : 'home';
  }
  if (path[0] === 'c') return 'category';
  return 'other';
}

/**
 * Whether the list a product was reached from was narrowed (`product_viewed`'s
 * `filtered`, F-6): the filters' own reading of the URL, so a parameter the
 * listing ignores does not count. False for a page that is not a list.
 */
export function cameFiltered(previous: UrlTree | null | undefined): boolean {
  if (!previous) return false;
  const kind = arrivedFrom(previous);
  return (kind === 'search' || kind === 'category' || kind === 'listing') && hasFilters(parseListing(previous.queryParamMap));
}
