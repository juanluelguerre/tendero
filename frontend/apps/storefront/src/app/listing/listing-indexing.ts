import { hasFilters, type ListingFilters, type ListingPlace } from './listing-filters';

/**
 * What a listing tells a search engine about itself (R-7.3, F-3). Three
 * answers, and the choice between them is the whole of the rule:
 *
 * - **A category** is a page worth ranking, filtered or not, and its canonical
 *   is the category with no filters. `/es/c/SHOES?brand=Vertex&f=COLOR:BLACK`
 *   is the shoes page narrowed, not a page of its own: every combination of
 *   filters would otherwise be a URL Google crawls, and a hundred products make
 *   thousands of them. The canonical folds them into the one that ranks. The
 *   order (`sort`) is folded the same way.
 * - **Words or filters with no category** — `/es?q=mochila`, `/es?offer=1` —
 *   are the shop's own search results, which Google asks sites not to let it
 *   index. `noindex,follow`: the page stays out, the products it links to are
 *   still found.
 * - **Neither** is the home page, which says nothing today and is left alone.
 *
 * Never both a canonical and `noindex` on one page: the first says "index that
 * other URL instead", the second "index nothing", and Google's advice is to
 * pick one because it may honour either.
 */
export type ListingIndexing =
  | { readonly kind: 'canonical'; readonly category: string }
  | { readonly kind: 'noindex' }
  | { readonly kind: 'silent' };

export function listingIndexing(place: ListingPlace, filters: ListingFilters): ListingIndexing {
  if (place.category) return { kind: 'canonical', category: place.category };
  if (place.query || hasFilters(filters)) return { kind: 'noindex' };
  return { kind: 'silent' };
}
