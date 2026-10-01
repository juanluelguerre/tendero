/** What the palette offers: where it goes, and what kind of thing it is. */
export interface PaletteOption {
  readonly id: string;
  readonly kind: 'order' | 'product' | 'agent' | 'screen';
  readonly label: string;
  readonly detail: string;
  readonly route: string;
  readonly queryParams: Record<string, string>;
}

/** Folded for comparison: no case, no accents — "cafe" finds "Cafeteras" and "Café". */
export function fold(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .trim();
}

/** Whether a label answers what was typed: every word typed appears in it, in any order. */
export function matches(label: string, query: string): boolean {
  const haystack = fold(label);
  return fold(query)
    .split(/\s+/)
    .filter(Boolean)
    .every((word) => haystack.includes(word));
}

/**
 * Whether what was typed looks like the start or end of an order's id: hex and
 * dashes, three characters at least. Then orders come first, because an id is
 * what nobody types by accident.
 */
export function looksLikeId(query: string): boolean {
  return /^[0-9a-f-]{3,}$/i.test(query.trim());
}

/** The next option, going round: down from the last is the first. */
export function step(index: number, count: number, by: 1 | -1): number {
  if (count === 0) return -1;
  return (index + by + count) % count;
}
