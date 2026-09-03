import type { ProductDetail, VariantView } from '@tendero/shared-api';

/**
 * What one option on one axis can be, given what is selected on the others.
 *
 * Three states and not two, and the difference between the last two is the whole
 * point of the picker:
 *
 * - `available` — the combination exists and can be bought.
 * - `soldOut` — the combination EXISTS and has no stock. It stays selectable, so
 *   the shopper can pick it, see that it is out and understand why. Hiding it
 *   would tell them their size is not made.
 * - `unavailable` — no such combination is sold, in any quantity. Disabled, and
 *   still rendered: an option that disappears makes the shopper think the shop
 *   lost it, and it makes the picker jump under their cursor.
 */
export type OptionAvailability = 'available' | 'soldOut' | 'unavailable';

/** Axis code to option code — one coordinate per axis. */
export type Selection = Readonly<Record<string, string>>;

/**
 * The variants a shopper can actually choose between.
 *
 * Discontinued ones are dropped here rather than filtered in three places. The
 * aggregate keeps them because an order placed last year still names one
 * (ADR 0002), and that is a different question from what is on sale today.
 */
export function sellable(product: ProductDetail): VariantView[] {
  return product.variants.filter((variant) => !variant.discontinued);
}

/** The variant a full selection names, or null when that combination is not sold. */
export function variantFor(variants: VariantView[], selection: Selection): VariantView | null {
  return (
    variants.find((variant) =>
      Object.entries(selection).every(([axis, option]) => variant.axisValues[axis] === option),
    ) ?? null
  );
}

/**
 * What would happen if this option were chosen, with every OTHER axis left where
 * it is.
 *
 * Holding the others fixed is what makes the answer useful. Asking "does any
 * variant have colour black" would light up black even when it does not exist in
 * the size already chosen, which is the false-positive facet ADR 0015 avoids in
 * search — the same mistake, one screen later.
 */
export function availabilityOf(
  variants: VariantView[],
  selection: Selection,
  axis: string,
  option: string,
): OptionAvailability {
  const candidate = { ...selection, [axis]: option };
  const matching = variants.filter((variant) =>
    Object.entries(candidate).every(([code, value]) => variant.axisValues[code] === value),
  );

  if (matching.length === 0) return 'unavailable';
  return matching.some((variant) => variant.inStock) ? 'available' : 'soldOut';
}

/**
 * Where the picker opens.
 *
 * A variant that can be bought wins over one that cannot, because a page that
 * opens on a sold-out combination reads as a shop with nothing in it. Falling
 * back to the first sellable variant keeps a fully out-of-stock product
 * rendering its picker instead of nothing at all.
 */
export function initialSelection(product: ProductDetail): Selection {
  const variants = sellable(product);
  const opening = variants.find((variant) => variant.inStock) ?? variants[0];

  return opening ? { ...opening.axisValues } : {};
}

/**
 * Choosing an option, and repairing the rest of the selection when it has to be.
 *
 * Picking a colour that does not come in the size already chosen would otherwise
 * leave the shopper on a combination that does not exist — a picker showing two
 * chosen options and no price. So the other axes move to the nearest thing that
 * does exist, preferring something in stock, and the axis that was actually
 * clicked never moves. A shop that quietly changes what you just pressed is
 * worse than one that changes the rest.
 */
export function select(
  variants: VariantView[],
  selection: Selection,
  axis: string,
  option: string,
): Selection {
  const candidate = { ...selection, [axis]: option };

  if (variantFor(variants, candidate)) return candidate;

  const nearest =
    variants.filter((variant) => variant.axisValues[axis] === option && variant.inStock)[0] ??
    variants.filter((variant) => variant.axisValues[axis] === option)[0];

  return nearest ? { ...nearest.axisValues } : candidate;
}
