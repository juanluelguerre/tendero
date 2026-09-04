/**
 * The artwork the shop puts on its own furniture.
 *
 * **A department needs a picture and a department is not a product**, which is
 * why this does not go anywhere near `IImageStore`. A product image is content:
 * it arrives from a source, it is addressed by the hash of its bytes (ADR 0011)
 * and the catalogue owns it. A department illustration is chrome — it belongs to
 * this interface, it changes when the design changes, and storing it in Postgres
 * would make redrawing a card a data migration.
 *
 * So it is a static asset, chosen by code, with a fallback that always exists.
 * There is no `(error)` handler and no broken tile: a code with no art of its
 * own resolves to the shop's own mark rather than to a request that 404s.
 *
 * **Swapping in photography is a file, not a change here.** Replace
 * `apparel.svg` with `apparel.webp` and adjust one line; nothing that consumes
 * this knows what is behind the path.
 */
const ART_BY_CODE: Readonly<Record<string, string>> = {
  APPAREL: 'apparel',
  HOME: 'home',
  BAGS: 'bags',
};

const FALLBACK = 'generic';

/** The illustration for a category code, or the shop's own if it has none. */
export function departmentArt(code: string): string {
  const file = ART_BY_CODE[code.toUpperCase()] ?? FALLBACK;
  return `/img/departments/${file}.svg`;
}

/** Whether a code has art of its own, for anything that should show nothing
 *  rather than the fallback. */
export function hasDepartmentArt(code: string): boolean {
  return code.toUpperCase() in ART_BY_CODE;
}

/**
 * The department a category belongs to, walking up parents.
 *
 * A section deep in the tree — `COFFEE_MAKER` — has no art and should not
 * invent one: it borrows the department's, because that is the thing a shopper
 * recognises. The walk is bounded by the list itself, and a cycle (which the
 * server's materialised path makes impossible) would still terminate.
 */
export function rootOf<T extends { code: string; parent?: string | null }>(
  code: string,
  categories: readonly T[],
): string {
  let current = categories.find((category) => category.code === code);
  const seen = new Set<string>();

  while (current?.parent && !seen.has(current.code)) {
    seen.add(current.code);
    const parent = categories.find((category) => category.code === current?.parent);
    if (!parent) break;
    current = parent;
  }

  return current?.code ?? code;
}
