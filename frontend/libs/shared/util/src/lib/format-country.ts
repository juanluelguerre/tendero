/**
 * A country's name, in the reader's culture, from its ISO 3166-1 code.
 *
 * The browser's to say and not the server's: the API runs with invariant
 * globalization, where a country has no name in any language, and a table of
 * names on either side would be one more list to keep. `Intl.DisplayNames`
 * already knows them all, in every language. A code it cannot name — two
 * letters typed before the form was a list — comes back as written.
 */
export function formatCountry(code: string, culture: string): string {
  const upper = code.trim().toUpperCase();
  if (!/^[A-Z]{2}$/.test(upper)) return code;

  return new Intl.DisplayNames([culture], { type: 'region', fallback: 'code' }).of(upper) ?? upper;
}
