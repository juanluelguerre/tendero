import { Pipe, type PipeTransform } from '@angular/core';
import {
  formatCountry,
  formatDate,
  formatDateTime,
  formatPrice,
  formatTime,
  type MoneyAmount,
} from '@tendero/shared-util';

/*
 * Money and moments in the reader's language, from a template (the review of
 * 2026-10-01, A7). Nineteen components each wrapped the shared formatter in a
 * method of their own, which a template called on every check of the view. A
 * pure pipe runs again only when an argument changes, so the language is one of
 * the arguments: `{{ total | price: culture.active() }}`, and a
 * switch of language reformats. Named so as not to shadow Angular's `currency`
 * and `date`, which read a locale this project does not register.
 */

/**
 * `{{ money | price: culture.active() }}` — "1.234,50 €" or "€1,234.50". The
 * money is what the API sent: minor units and its currency (phase 13b).
 */
@Pipe({ name: 'price' })
export class PricePipe implements PipeTransform {
  transform(money: MoneyAmount, culture: string): string {
    return formatPrice(money, culture);
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

/** A country by name from its code: `{{ address.countryCode | country: culture.active() }}` — "Alemania", "Germany". */
@Pipe({ name: 'country' })
export class CountryPipe implements PipeTransform {
  transform(code: string, culture: string): string {
    return formatCountry(code, culture);
  }
}
