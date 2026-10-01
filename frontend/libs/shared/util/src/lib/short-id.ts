/**
 * A long identifier as a person reads and matches it: its last eight
 * characters, dashes left out.
 *
 * The end, never the start. A version-7 Guid begins with a timestamp, so ids
 * minted within a minute or so share their first segment; a dashless id — a
 * trace id — has no segment at all, and "the first segment" of it was the whole
 * value. The end is random in both, and tells two of them apart.
 */
export function shortId(id: string): string {
  return id.replace(/-/g, '').slice(-8);
}
