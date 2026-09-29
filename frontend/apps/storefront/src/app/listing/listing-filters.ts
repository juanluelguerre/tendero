import type { Params } from '@angular/router';
import type { FacetKind, ProductSort } from '@tendero/shared-api';

/**
 * What a listing is narrowed by, and how it lives in the URL (R-7.3, F-3).
 *
 * **The URL is the state.** A filtered list can be shared, a reload keeps it and
 * the back button undoes one choice, because every choice is a navigation and
 * nothing is kept anywhere else. These functions are the only place that knows
 * the parameters' names, and they are pure, so the whole mapping is testable
 * without a router.
 *
 * The parameters, short because people read and share them, and stable because
 * links outlive the code that wrote them:
 *
 * | Parameter | Holds | API parameter |
 * |---|---|---|
 * | `brand`  | a brand, repeated for several (OR) | `brand` |
 * | `within` | a category code inside a search, repeated (OR) | `within` |
 * | `f`      | `CODE:VALUE` of an axis, attribute or flag, repeated | `f` |
 * | `price`  | `30-60`, `30-` or `-60`, both ends inclusive | `minPrice`, `maxPrice` |
 * | `offer`  | `1`: on offer now | `onOffer` |
 * | `new`    | `1`: listed in the last 30 days | `newArrivals` |
 * | `stock`  | `1`: can be bought now | `inStock` |
 * | `sort`   | `newest`, `price-asc` or `price-desc`; absent is relevance | `sort` |
 * | `campaign` | a campaign's code: what its promotions reach now (R-8) | `campaign` |
 *
 * Repeated parameters rather than the spec's comma lists: a value is a brand or
 * an option code the shop does not control, and one day one of them holds a
 * comma. The price is the range and never a band, because bands are computed
 * from the results and move with them, and a link must not.
 */
export const ListingParam = {
  brand: 'brand',
  within: 'within',
  value: 'f',
  price: 'price',
  offer: 'offer',
  newArrivals: 'new',
  inStock: 'stock',
  sort: 'sort',
  campaign: 'campaign',
} as const;

export const ListingSorts = ['price-asc', 'price-desc', 'newest'] as const;
export type ListingSort = (typeof ListingSorts)[number];

/**
 * The shop's address says `?sort=price-asc`, a word a person reads in a link;
 * the API takes its enum (`PriceAsc`). One table between them, typed on both
 * sides, so a new order on either end fails to compile here rather than
 * reaching the API as a 400.
 */
const ApiSorts: Record<ListingSort, ProductSort> = {
  'price-asc': 'PriceAsc',
  'price-desc': 'PriceDesc',
  newest: 'Newest',
};

export interface ListingFilters {
  readonly brands: readonly string[];
  readonly within: readonly string[];
  /** `CODE:VALUE` pairs: axes, attributes and flags, as the API takes them. */
  readonly values: readonly string[];
  readonly minPrice: number | null;
  readonly maxPrice: number | null;
  readonly onOffer: boolean;
  readonly newArrivals: boolean;
  readonly inStock: boolean;
  /** Null is the default order: relevance on a search, recommended on a category. */
  readonly sort: ListingSort | null;
  /** A campaign's code: the campaign card's *Ver las ofertas* (R-8, H-2). */
  readonly campaign: string | null;
}

export const NoFilters: ListingFilters = {
  brands: [],
  within: [],
  values: [],
  minPrice: null,
  maxPrice: null,
  onOffer: false,
  newArrivals: false,
  inStock: false,
  sort: null,
  campaign: null,
};

/** What a listing lists: words, a place in the taxonomy, both or neither. */
export interface ListingPlace {
  readonly query: string;
  readonly category: string | null;
}

/** The part of Angular's `ParamMap` these functions read, so a test can hand in a plain object. */
export interface ReadableParams {
  get(name: string): string | null;
  getAll(name: string): string[];
}

// ---------- Reading and writing the URL ----------

export function parseListing(params: ReadableParams): ListingFilters {
  const [minPrice, maxPrice] = parsePrice(params.get(ListingParam.price));
  const sort = params.get(ListingParam.sort);

  return {
    brands: distinct(params.getAll(ListingParam.brand)),
    within: distinct(params.getAll(ListingParam.within)),
    // A value without its code cannot be asked for; the API would refuse it.
    values: distinct(params.getAll(ListingParam.value)).filter((pair) => pair.indexOf(':') > 0),
    minPrice,
    maxPrice,
    onOffer: isOn(params.get(ListingParam.offer)),
    newArrivals: isOn(params.get(ListingParam.newArrivals)),
    inStock: isOn(params.get(ListingParam.inStock)),
    sort: (ListingSorts as readonly string[]).includes(sort ?? '') ? (sort as ListingSort) : null,
    campaign: campaignCode(params.get(ListingParam.campaign)),
  };
}

/**
 * Every listing parameter, `null` where there is nothing to say, for a
 * navigation that merges them: `null` removes a parameter, so the address never
 * carries an empty `brand=` and never keeps a filter that was taken off.
 */
export function listingQueryParams(filters: ListingFilters): Params {
  return {
    [ListingParam.brand]: orNull(filters.brands),
    [ListingParam.within]: orNull(filters.within),
    [ListingParam.value]: orNull(filters.values),
    [ListingParam.price]: priceParam(filters.minPrice, filters.maxPrice),
    [ListingParam.offer]: filters.onOffer ? '1' : null,
    [ListingParam.newArrivals]: filters.newArrivals ? '1' : null,
    [ListingParam.inStock]: filters.inStock ? '1' : null,
    [ListingParam.sort]: filters.sort,
    [ListingParam.campaign]: filters.campaign,
  };
}

/**
 * `GET /api/search`'s parameters for one page of a listing, as `HttpParams`'
 * `fromObject` takes them: arrays become repeated parameters. Only what was
 * asked for is sent.
 */
export function searchParams(
  place: ListingPlace,
  filters: ListingFilters,
  culture: string,
  page: number,
  pageSize: number,
): Record<string, string | string[]> {
  const params: Record<string, string | string[]> = { culture, pageSize: String(pageSize) };

  if (place.query) params['q'] = place.query;
  if (place.category) params['category'] = place.category;
  if (page > 1) params['page'] = String(page);
  if (filters.sort) params['sort'] = ApiSorts[filters.sort];
  if (filters.brands.length) params['brand'] = [...filters.brands];
  // A category page is a place; narrowing inside it by category is navigation, not `within`.
  if (filters.within.length && !place.category) params['within'] = [...filters.within];
  if (filters.values.length) params['f'] = [...filters.values];
  if (filters.minPrice !== null) params['minPrice'] = String(filters.minPrice);
  if (filters.maxPrice !== null) params['maxPrice'] = String(filters.maxPrice);
  if (filters.onOffer) params['onOffer'] = 'true';
  if (filters.newArrivals) params['newArrivals'] = 'true';
  if (filters.inStock) params['inStock'] = 'true';
  if (filters.campaign) params['campaign'] = filters.campaign;

  return params;
}

// ---------- Changing them ----------

/** Whether a facet's option is chosen in these filters. The screen reads this, not
 *  the server's `chosen`, so a batch in the phone's sheet shows its own choices
 *  before the server has answered for them. */
export function isChosen(filters: ListingFilters, kind: FacetKind, value: string): boolean {
  switch (kind) {
    case 'Category':
      return filters.within.includes(value);
    case 'Brand':
      return filters.brands.includes(value);
    case 'Offer':
      return filters.onOffer;
    case 'New':
      return filters.newArrivals;
    case 'InStock':
      return filters.inStock;
    case 'Axis':
    case 'Attribute':
    case 'Flag':
      return filters.values.includes(value);
    case 'Price':
      return false;
  }
}

/** One option on or off. Within a facet the choices add up; across facets they narrow (the server's rule). */
export function toggleOption(filters: ListingFilters, kind: FacetKind, value: string): ListingFilters {
  switch (kind) {
    case 'Category':
      return { ...filters, within: toggled(filters.within, value) };
    case 'Brand':
      return { ...filters, brands: toggled(filters.brands, value) };
    case 'Offer':
      return { ...filters, onOffer: !filters.onOffer };
    case 'New':
      return { ...filters, newArrivals: !filters.newArrivals };
    case 'InStock':
      return { ...filters, inStock: !filters.inStock };
    case 'Axis':
    case 'Attribute':
    case 'Flag':
      return { ...filters, values: toggled(filters.values, value) };
    case 'Price':
      return filters;
  }
}

/** The range, both ends inclusive; either may be open. Reversed ends are put the right way round. */
export function withPrice(filters: ListingFilters, min: number | null, max: number | null): ListingFilters {
  const low = validPrice(min);
  const high = validPrice(max);
  if (low !== null && high !== null && low > high) return { ...filters, minPrice: high, maxPrice: low };
  return { ...filters, minPrice: low, maxPrice: high };
}

export function withSort(filters: ListingFilters, sort: ListingSort | null): ListingFilters {
  return { ...filters, sort };
}

/** *Quitar todo*: every filter off — the campaign too, which is a filter like the others. The order is not a filter, so it stays. */
export function clearAll(filters: ListingFilters): ListingFilters {
  return { ...NoFilters, sort: filters.sort };
}

/** How many filters are applied: one per chip, which is what *Filtros (n)* counts. */
export function appliedCount(filters: ListingFilters): number {
  return (
    filters.brands.length +
    filters.within.length +
    filters.values.length +
    (filters.minPrice !== null || filters.maxPrice !== null ? 1 : 0) +
    (filters.onOffer ? 1 : 0) +
    (filters.newArrivals ? 1 : 0) +
    (filters.inStock ? 1 : 0) +
    (filters.campaign ? 1 : 0)
  );
}

export function hasFilters(filters: ListingFilters): boolean {
  return appliedCount(filters) > 0;
}

/**
 * The facets whose choices a change narrowed or moved, by the facet's code —
 * what `filter_applied` counts (F-6). A facet taken off entirely is not in it:
 * undoing a choice is not choosing one, and *Quitar todo* has its own event.
 * An attribute is its catalogue code (`COLOR`), never the value chosen.
 */
export function narrowedFacets(before: ListingFilters, after: ListingFilters): string[] {
  const changed = (a: readonly string[], b: readonly string[]) => b.length > 0 && !sameSet(a, b);
  const facets: string[] = [];

  if (changed(before.within, after.within)) facets.push('category');
  if (changed(before.brands, after.brands)) facets.push('brand');
  if ((after.minPrice !== null || after.maxPrice !== null) && (after.minPrice !== before.minPrice || after.maxPrice !== before.maxPrice)) {
    facets.push('price');
  }
  if (after.onOffer && !before.onOffer) facets.push('offer');
  if (after.newArrivals && !before.newArrivals) facets.push('new');
  if (after.inStock && !before.inStock) facets.push('stock');

  const codes = new Set(after.values.map((value) => value.slice(0, value.indexOf(':'))));
  for (const code of codes) {
    const of = (values: readonly string[]) => values.filter((value) => value.startsWith(`${code}:`));
    if (changed(of(before.values), of(after.values))) facets.push(code);
  }

  return facets;
}

/** Whether two sets of filters ask the same question; the order of choices does not matter. */
export function sameFilters(a: ListingFilters, b: ListingFilters): boolean {
  return (
    sameSet(a.brands, b.brands) &&
    sameSet(a.within, b.within) &&
    sameSet(a.values, b.values) &&
    a.minPrice === b.minPrice &&
    a.maxPrice === b.maxPrice &&
    a.onOffer === b.onOffer &&
    a.newArrivals === b.newArrivals &&
    a.inStock === b.inStock &&
    a.sort === b.sort &&
    a.campaign === b.campaign
  );
}

/** A price typed into a box: a number, a comma for the decimal as a Spanish keyboard writes it, or nothing. */
export function parseAmount(text: string): number | null {
  const trimmed = text.trim().replace(',', '.');
  if (!trimmed) return null;
  const amount = Number(trimmed);
  return validPrice(Number.isFinite(amount) ? amount : null);
}

// ---------- Helpers ----------

function parsePrice(text: string | null): [number | null, number | null] {
  const match = /^(\d+(?:\.\d+)?)?-(\d+(?:\.\d+)?)?$/.exec(text?.trim() ?? '');
  if (!match) return [null, null];
  const range = withPrice(NoFilters, match[1] ? Number(match[1]) : null, match[2] ? Number(match[2]) : null);
  return [range.minPrice, range.maxPrice];
}

function priceParam(min: number | null, max: number | null): string | null {
  if (min === null && max === null) return null;
  return `${min ?? ''}-${max ?? ''}`;
}

function validPrice(amount: number | null): number | null {
  return amount !== null && Number.isFinite(amount) && amount >= 0 ? amount : null;
}

/** A campaign's code as the shopkeeper writes them (`TENDERO_DAYS`); anything else is no campaign. */
function campaignCode(value: string | null): string | null {
  const code = value?.trim().toUpperCase() ?? '';
  return /^[A-Z0-9_-]{1,40}$/.test(code) ? code : null;
}

function isOn(value: string | null): boolean {
  return value === '1' || value === 'true';
}

function distinct(values: readonly string[]): string[] {
  return [...new Set(values.map((value) => value.trim()).filter(Boolean))];
}

function toggled(values: readonly string[], value: string): string[] {
  return values.includes(value) ? values.filter((item) => item !== value) : [...values, value];
}

function orNull(values: readonly string[]): string[] | null {
  return values.length ? [...values] : null;
}

function sameSet(a: readonly string[], b: readonly string[]): boolean {
  return a.length === b.length && a.every((value) => b.includes(value));
}
