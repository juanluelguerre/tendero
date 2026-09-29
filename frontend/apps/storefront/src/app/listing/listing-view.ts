import type { CategoryView, FacetOption, SearchFacet } from '@tendero/shared-api';
import { isChosen, toggleOption, withPrice, type ListingFilters } from './listing-filters';

/**
 * How the facets the API returns are drawn: which section each becomes, which
 * brands show, which level of the taxonomy a search offers, and the applied
 * chips. Pure, like the URL functions beside it, so the rules are tested
 * without a browser; the components only render what these decide.
 */

// ---------- Sections ----------

/** The switches that share one section, as the mock-ups draw them: *Ofertas y disponibilidad*. */
const SwitchKinds: readonly SearchFacet['kind'][] = ['Offer', 'New', 'InStock'];

export type FacetSection =
  | { readonly kind: 'category' | 'brand' | 'price' | 'swatches' | 'sizes' | 'checks' | 'flag'; readonly id: string; readonly facet: SearchFacet }
  | { readonly kind: 'switches'; readonly id: string; readonly facets: readonly SearchFacet[] };

/**
 * The column's sections, in the server's order (category, brand, price, the
 * switches, then the attributes in their definitions' order). Each facet is
 * drawn as what it is: a colour as a swatch with its name, a size as a button
 * in a grid, a yes-or-no as a switch.
 */
export function sectionsOf(facets: readonly SearchFacet[]): FacetSection[] {
  const sections: FacetSection[] = [];
  const switches = facets.filter((facet) => SwitchKinds.includes(facet.kind));

  for (const facet of facets) {
    const id = `facet-${facet.code}`;
    switch (facet.kind) {
      case 'Category':
        sections.push({ kind: 'category', id, facet });
        break;
      case 'Brand':
        sections.push({ kind: 'brand', id, facet });
        break;
      case 'Price':
        if (facet.price) sections.push({ kind: 'price', id, facet });
        break;
      case 'Offer':
      case 'New':
      case 'InStock':
        // One section for the three, where the first of them stands.
        if (facet === switches[0]) sections.push({ kind: 'switches', id: 'facet-switches', facets: switches });
        break;
      case 'Flag':
        sections.push({ kind: 'flag', id, facet });
        break;
      case 'Axis':
      case 'Attribute':
        sections.push({
          kind: facet.options.some((option) => safeSwatch(option.swatch)) ? 'swatches' : facet.kind === 'Axis' ? 'sizes' : 'checks',
          id,
          facet,
        });
        break;
    }
  }

  return sections;
}

/**
 * A swatch colour the page may paint, or null. It comes from the shop's data
 * (the attribute definition), never from a stylesheet, and only a plain
 * `#RRGGBB` is let through to a style binding.
 */
export function safeSwatch(swatch: string | null | undefined): string | null {
  return swatch && /^#[0-9a-f]{6}$/i.test(swatch) ? swatch : null;
}

/** An option nobody can choose: nothing left with it and not already chosen. Shown disabled, never hidden. */
export function isDead(option: FacetOption, chosen: boolean): boolean {
  return option.count === 0 && !chosen;
}

// ---------- Brand, at the scale of hundreds ----------

/** The brands shown before *Ver más marcas*. */
export const TopBrands = 7;

/** Past this many brands, a box to find one. */
export const FindableBrandsFrom = 20;

export interface BrandView {
  readonly shown: readonly FacetOption[];
  /** How many more *Ver más marcas* would show. */
  readonly hidden: number;
  readonly findable: boolean;
}

/**
 * Which brands show: the seven with most results first (the server's order is
 * by count), every chosen one whatever its rank, all of them once expanded, and
 * those whose name holds what was typed once there are enough to need a box.
 */
export function brandsToShow(
  options: readonly FacetOption[],
  filters: ListingFilters,
  expanded: boolean,
  find: string,
): BrandView {
  const findable = options.length > FindableBrandsFrom;
  const needle = fold(find);

  if (findable && needle) {
    const shown = options.filter((option) => fold(option.label).includes(needle));
    return { shown, hidden: 0, findable };
  }

  if (expanded || options.length <= TopBrands) return { shown: options, hidden: 0, findable };

  const shown = options.filter((option, index) => index < TopBrands || isChosen(filters, 'Brand', option.value));
  return { shown, hidden: options.length - shown.length, findable };
}

// ---------- The category, on a search and on a category page ----------

export interface CategoryLevel {
  /** Departments, or the categories of the one department the results are in. */
  readonly depth: number;
  readonly options: readonly FacetOption[];
}

/**
 * The taxonomy narrowed to where a search's results are (Amazon's *En Moda
 * (34) · En Hogar (12)*). The API counts every code; the screen picks the
 * level: the departments, and when the results sit in one department only, the
 * level below it, and so on. A chosen option always stays, so it can be unchosen.
 */
export function categoryLevel(
  facet: SearchFacet,
  categories: readonly CategoryView[],
  filters: ListingFilters,
): CategoryLevel {
  const byCode = new Map(categories.map((category) => [category.code, category]));
  const counted = facet.options.filter((option) => byCode.has(option.value));
  const inLevel = (parent: string | null) =>
    counted.filter((option) => (byCode.get(option.value)?.parent ?? null) === parent);

  let depth = 0;
  let level = inLevel(null);

  for (;;) {
    const holding = level.filter((option) => option.count > 0);
    if (holding.length !== 1) break;
    const below = inLevel(holding[0].value);
    if (below.length === 0) break;
    level = below;
    depth++;
  }

  const chosen = facet.options.filter(
    (option) => isChosen(filters, 'Category', option.value) && !level.includes(option),
  );
  return { depth, options: [...level, ...chosen] };
}

export interface CategoryLink {
  readonly code: string;
  readonly name: string;
  /** Null when the API did not count the category (its facet did not split the results). */
  readonly count: number | null;
}

/**
 * A category page's sections as links, with the counts the category facet gave
 * them. A section is a place with its own URL, not a filter (the spec: a
 * category stays single-select).
 */
export function childLinks(
  code: string,
  categories: readonly CategoryView[],
  facets: readonly SearchFacet[],
): CategoryLink[] {
  const facet = facets.find((item) => item.kind === 'Category');
  const counts = new Map(facet?.options.map((option) => [option.value, option.count]) ?? []);

  return categories
    .filter((category) => category.parent === code)
    .map((category) => ({
      code: category.code,
      name: category.name,
      count: facet ? (counts.get(category.code) ?? 0) : null,
    }));
}

// ---------- Nothing moves under the pointer ----------

/**
 * The next answer's facets, keeping the options the previous answer had in the
 * facets both hold, at zero and in their old places.
 *
 * The API returns the options that still have products; an option that just
 * reached zero would otherwise vanish from under the pointer, and every option
 * below it would move up. Zero is exactly true for such an option, so it is
 * kept and drawn disabled. A whole facet that vanished is NOT kept: the server
 * dropped it because it no longer splits the results, and its counts are
 * unknown rather than zero.
 */
export function keepShape(previous: readonly SearchFacet[], next: readonly SearchFacet[]): SearchFacet[] {
  const before = new Map(previous.map((facet) => [facet.code, facet]));

  return next.map((facet) => {
    const old = before.get(facet.code);
    if (!old || facet.kind === 'Price' || SwitchKinds.includes(facet.kind) || facet.kind === 'Flag') return facet;

    const fresh = new Map(facet.options.map((option) => [option.value, option]));
    const kept = old.options.map(
      (option) => fresh.get(option.value) ?? { ...option, count: 0, chosen: false },
    );
    const added = facet.options.filter((option) => !old.options.some((item) => item.value === option.value));
    return { ...facet, options: [...kept, ...added] };
  });
}

// ---------- Price ----------

/** How a listing writes a price range: a formatter is handed in, so this stays pure. */
export interface PriceWords {
  upTo(max: string): string;
  from(min: string): string;
  between(min: string, max: string): string;
}

export function priceLabel(
  min: number | null,
  max: number | null,
  money: (amount: number) => string,
  words: PriceWords,
): string {
  if (min !== null && max !== null) return words.between(money(min), money(max));
  if (max !== null) return words.upTo(money(max));
  if (min !== null) return words.from(money(min));
  return '';
}

/**
 * A price the way a person says it: no cents when there are none ("30 €",
 * never "30,00 €"), and the cents when there are.
 */
export function sayablePrice(amount: number, currency: string, culture: string): string {
  const whole = Number.isInteger(amount);
  return new Intl.NumberFormat(culture, {
    style: 'currency',
    currency,
    minimumFractionDigits: whole ? 0 : 2,
    maximumFractionDigits: whole ? 0 : 2,
  }).format(amount);
}

// ---------- The applied chips ----------

export interface Chip {
  readonly id: string;
  readonly label: string;
  /** The filters with this one taken off: removing a chip is a navigation to them. */
  readonly without: ListingFilters;
}

export interface ChipWords extends PriceWords {
  readonly onOffer: string;
  readonly newArrivals: string;
  readonly inStock: string;
  money(amount: number): string;
  /** A campaign's chip, by its name when the shop knows it (R-8). */
  campaign(code: string): string;
}

/**
 * One chip per applied filter, labelled as the column labels it: a category
 * and a brand by name, an option by its localized label, a price as its range.
 * The labels come from the facets; a value no facet names any more (a stale
 * link, a brand gone) still gets a chip with its raw value, because it is still
 * narrowing the list and the shopper has to be able to take it off.
 */
export function chipsOf(filters: ListingFilters, facets: readonly SearchFacet[], words: ChipWords): Chip[] {
  const labelOf = (kind: SearchFacet['kind'], value: string) =>
    facets.find((facet) => facet.kind === kind && facet.options.some((option) => option.value === value))
      ?.options.find((option) => option.value === value)?.label;

  const switchLabel = (kind: SearchFacet['kind'], fallback: string) =>
    facets.find((facet) => facet.kind === kind)?.label ?? fallback;

  const chips: Chip[] = [
    ...(filters.campaign ? [{ id: 'campaign', label: words.campaign(filters.campaign), without: { ...filters, campaign: null } }] : []),
    ...filters.within.map((code) => ({
      id: `within:${code}`,
      label: labelOf('Category', code) ?? code,
      without: toggleOption(filters, 'Category', code),
    })),
    ...filters.brands.map((brand) => ({
      id: `brand:${brand}`,
      label: labelOf('Brand', brand) ?? brand,
      without: toggleOption(filters, 'Brand', brand),
    })),
  ];

  if (filters.minPrice !== null || filters.maxPrice !== null) {
    chips.push({
      id: 'price',
      label: priceLabel(filters.minPrice, filters.maxPrice, words.money, words),
      without: withPrice(filters, null, null),
    });
  }

  if (filters.onOffer) chips.push({ id: 'offer', label: switchLabel('Offer', words.onOffer), without: toggleOption(filters, 'Offer', 'true') });
  if (filters.newArrivals) chips.push({ id: 'new', label: switchLabel('New', words.newArrivals), without: toggleOption(filters, 'New', 'true') });
  if (filters.inStock) chips.push({ id: 'stock', label: switchLabel('InStock', words.inStock), without: toggleOption(filters, 'InStock', 'true') });

  // Axes, attributes and flags in the column's order, then any the facets no longer name.
  const order = facets.flatMap((facet) => facet.options.map((option) => option.value));
  const values = [...filters.values].sort((a, b) => rank(order, a) - rank(order, b));
  for (const pair of values) {
    chips.push({
      id: `f:${pair}`,
      label: labelOf('Axis', pair) ?? labelOf('Attribute', pair) ?? labelOf('Flag', pair) ?? pair.slice(pair.indexOf(':') + 1),
      without: toggleOption(filters, 'Attribute', pair),
    });
  }

  return chips;
}

function rank(order: readonly string[], value: string): number {
  const index = order.indexOf(value);
  return index < 0 ? Number.MAX_SAFE_INTEGER : index;
}

/** Case and accents folded, so "cami" finds "Caminante" and "nordw" finds "Nordwind". */
function fold(text: string): string {
  return text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase().trim();
}
