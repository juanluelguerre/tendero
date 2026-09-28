import type { SearchFacet } from '@tendero/shared-api';
import {
  withPrice,
  type ListingFilters,
  type ListingPlace,
} from '../listing/listing-filters';

/**
 * The browser agent's filters (R-7.3, F-5): what `list_filters` says and what
 * `apply_filter` does, as pure functions, so the rule is tested without a
 * browser and the tools in `storefront-tools.ts` stay a thin call.
 *
 * **The agent speaks the facets' own words.** A facet's `code` (`brand`,
 * `COLOR`, `offer`) and an option's `value` (`Vertex`, `COLOR:BLACK`) are what
 * the column sends and what the URL holds; the tool takes exactly those, so
 * nothing is translated and a choice the agent makes is the one a click makes.
 * Codes, not labels: *negro* and *black* are the same option in two languages.
 */

/** What `apply_filter` receives. `values` replaces the facet's choices; empty clears them. */
export interface FilterChoice {
  readonly facet: string;
  readonly values?: readonly string[];
  readonly minPrice?: number | null;
  readonly maxPrice?: number | null;
}

export type FilterOutcome =
  | { readonly ok: true; readonly filters: ListingFilters }
  | { readonly ok: false; readonly reason: string };

/** Every facet the list offers, one per line, with the value to send for each option and how many it would leave. */
export function describeFacets(facets: readonly SearchFacet[], total: number): string {
  if (facets.length === 0) return `${total} product(s), and nothing to narrow them by.`;

  const lines = facets.map((facet) => {
    if (facet.kind === 'Price' && facet.price) {
      const { min, max, chosenMin, chosenMax } = facet.price;
      const chosen = chosenMin !== null || chosenMax !== null ? ` (chosen: ${chosenMin ?? ''}–${chosenMax ?? ''})` : '';
      return `- ${facet.code} (${facet.label}): from ${min} to ${max}; send minPrice and/or maxPrice${chosen}`;
    }

    const options = facet.options
      .map((option) => `${option.value}${option.value === option.label ? '' : ` "${option.label}"`} [${option.count}]${option.chosen ? ' (chosen)' : ''}`)
      .join(', ');
    return `- ${facet.code} (${facet.label}): ${options}`;
  });

  return `${total} product(s). Narrow them with apply_filter, by facet code and option value:\n${lines.join('\n')}`;
}

/**
 * The filters a choice leaves, or why it cannot be made. Only what the list
 * on screen offers can be chosen: a brand it does not carry would narrow to
 * nothing, and an agent is better told than shown an empty page.
 */
export function chooseFilter(
  filters: ListingFilters,
  facets: readonly SearchFacet[],
  place: ListingPlace,
  choice: FilterChoice,
): FilterOutcome {
  const facet = facets.find((candidate) => candidate.code === choice.facet);
  if (!facet) {
    return refused(`No facet "${choice.facet}" here. The list offers: ${facets.map((f) => f.code).join(', ') || 'none'}.`);
  }

  if (facet.kind === 'Price') {
    const min = choice.minPrice ?? null;
    const max = choice.maxPrice ?? null;
    if ((min !== null && !(min >= 0)) || (max !== null && !(max >= 0))) return refused('A price is a number of at least 0.');
    return { ok: true, filters: withPrice(filters, min, max) };
  }

  const values = [...new Set(choice.values ?? [])];
  const unknown = values.filter((value) => !facet.options.some((option) => option.value === value));
  if (unknown.length > 0) {
    return refused(
      `${facet.code} has no option ${unknown.map((value) => `"${value}"`).join(', ')}. ` +
        `Its options: ${facet.options.map((option) => option.value).join(', ')}.`,
    );
  }

  switch (facet.kind) {
    case 'Category':
      // On a category page its sections are pages, reached by a link; the
      // filter narrows a SEARCH by category.
      if (place.category) return refused('On a category page the sections are links, not filters.');
      return { ok: true, filters: { ...filters, within: values } };
    case 'Brand':
      return { ok: true, filters: { ...filters, brands: values } };
    case 'Offer':
      return { ok: true, filters: { ...filters, onOffer: values.length > 0 } };
    case 'New':
      return { ok: true, filters: { ...filters, newArrivals: values.length > 0 } };
    case 'InStock':
      return { ok: true, filters: { ...filters, inStock: values.length > 0 } };
    case 'Axis':
    case 'Attribute':
    case 'Flag': {
      // The facet's own pairs go, the others stay: COLOR is replaced, SIZE is not.
      const others = filters.values.filter((value) => !value.startsWith(`${facet.code}:`));
      return { ok: true, filters: { ...filters, values: [...others, ...values] } };
    }
  }
}

function refused(reason: string): FilterOutcome {
  return { ok: false, reason };
}
