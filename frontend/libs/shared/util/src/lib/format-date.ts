/**
 * A date and time, in the reader's culture.
 *
 * Shared for the same reason `formatPrice` is: the ORDER of day and month, the
 * separator and the clock all change with the culture, and `Intl` already knows
 * how — 04/09/2026 and 09/04/2026 are the same instant and different days.
 *
 * `Intl.DateTimeFormat` rather than Angular's `DatePipe`, and it is not just
 * consistency with the price helper: the pipe needs every non-English locale
 * registered by hand at bootstrap, and a locale nobody registered fails by
 * silently formatting in English. This has nothing to forget.
 */
export function formatDateTime(value: string | Date, culture: string): string {
  const instant = typeof value === 'string' ? new Date(value) : value;

  return new Intl.DateTimeFormat(culture, {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(instant);
}

/** The same, without the clock. For a list where the day is the answer. */
export function formatDate(value: string | Date, culture: string): string {
  const instant = typeof value === 'string' ? new Date(value) : value;

  return new Intl.DateTimeFormat(culture, { dateStyle: 'medium' }).format(instant);
}
