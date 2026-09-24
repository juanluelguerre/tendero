/**
 * A URL as it may be recorded: no query string, no fragment, and no segment that
 * could identify a person or unlock something.
 *
 * The query goes because it carries what the shopper typed — a search box is a
 * free-text field, and people type names into free-text fields. And a path
 * segment goes when it looks like a key rather than a word: `/api/orders/{orderId}`
 * names one customer's order, and a trace store is not where that belongs — it
 * also turns every order into a distinct URL, which is cardinality nobody can
 * query. Product codes (`B13BAG0710`) and slugs stay; they describe the shop,
 * not the customer. (The cart's bearer token travels in a header, never in a
 * path, which is why it needs no rule here.)
 */
export function scrubUrl(url: string): string {
  const [withoutFragment] = url.split('#');
  const [path] = (withoutFragment ?? '').split('?');

  return (path ?? '')
    .split('/')
    .map((segment) => (looksLikeASecret(segment) ? ':id' : segment))
    .join('/');
}

function looksLikeASecret(segment: string): boolean {
  // A GUID, with or without dashes.
  if (/^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i.test(segment)) return true;

  // A long run of key material: hex, or base64url, twenty characters or more
  // with no dash between words — a slug has dashes, a token does not.
  return /^[A-Za-z0-9_]{20,}$/.test(segment) && /\d/.test(segment);
}
