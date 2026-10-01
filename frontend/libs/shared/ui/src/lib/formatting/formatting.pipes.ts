import { Pipe, type PipeTransform } from '@angular/core';
import { formatDate, formatDateTime, formatPrice, formatTime } from '@tendero/shared-util';

/*
 * Money and moments in the reader's language, from a template (the review of
 * 2026-10-01, A7). Nineteen components each wrapped the shared formatter in a
 * method of their own, which a template called on every check of the view. A
 * pure pipe runs again only when an argument changes, so the language is one of
 * the arguments: `{{ total | price: currency : culture.active() }}`, and a
 * switch of language reformats. Named so as not to shadow Angular's `currency`
 * and `date`, which read a locale this project does not register.
 */

/** `{{ amount | price: 'EUR' : culture.active() }}` — "1.234,50 €" or "€1,234.50". */
@Pipe({ name: 'price' })
export class PricePipe implements PipeTransform {
  transform(amount: number, currency: string, culture: string): string {
    return formatPrice(amount, currency, culture);
  }
}

/** A day and a time, short: an order placed, an entry in a log. */
@Pipe({ name: 'dateTime' })
export class DateTimePipe implements PipeTransform {
  transform(value: string | Date, culture: string): string {
    return formatDateTime(value, culture);
  }
}

/** A day alone, for a list where the day is the answer. */
@Pipe({ name: 'day' })
export class DayPipe implements PipeTransform {
  transform(value: string | Date, culture: string): string {
    return formatDate(value, culture);
  }
}

/** The clock alone: "when it was last updated" on a wall. */
@Pipe({ name: 'clock' })
export class ClockPipe implements PipeTransform {
  transform(value: string | Date, culture: string): string {
    return formatTime(value, culture);
  }
}
