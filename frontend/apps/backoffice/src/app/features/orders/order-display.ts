/** How an order is named and dated on screen: the list and the pane say it the same way. */

/** "2 days ago", in the reader's language. */
export function placedAgo(iso: string, culture: string): string {
  const minutes = Math.round((Date.parse(iso) - Date.now()) / 60_000);
  const format = new Intl.RelativeTimeFormat(culture, { numeric: 'auto', style: 'short' });
  if (Math.abs(minutes) < 60) return format.format(minutes, 'minute');
  if (Math.abs(minutes) < 60 * 24) return format.format(Math.round(minutes / 60), 'hour');
  return format.format(Math.round(minutes / (60 * 24)), 'day');
}
