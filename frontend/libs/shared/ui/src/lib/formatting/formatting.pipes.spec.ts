import { describe, expect, it } from 'vitest';
import { ClockPipe, DateTimePipe, DayPipe, PricePipe } from './formatting.pipes';

/**
 * Money and moments in the reader's language, from a template (the review of
 * 2026-10-01, A7). Pure pipes: they run again only when an argument changes —
 * which is why the language is one of the arguments, so a switch reformats.
 */
describe('the formatting pipes', () => {
  const at = '2026-10-01T09:05:00Z';

  it('write a price as the language writes money', () => {
    const price = new PricePipe();

    expect([price.transform(1234.5, 'EUR', 'es'), price.transform(1234.5, 'EUR', 'en')]).toEqual([
      new Intl.NumberFormat('es', { style: 'currency', currency: 'EUR' }).format(1234.5),
      new Intl.NumberFormat('en', { style: 'currency', currency: 'EUR' }).format(1234.5),
    ]);
  });

  it('write a moment as a day, as a day and a time, or as a time', () => {
    expect([
      new DayPipe().transform(at, 'es'),
      new DateTimePipe().transform(at, 'es'),
      new ClockPipe().transform(at, 'es'),
    ]).toEqual([
      new Intl.DateTimeFormat('es', { dateStyle: 'medium' }).format(new Date(at)),
      new Intl.DateTimeFormat('es', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(at)),
      new Intl.DateTimeFormat('es', { timeStyle: 'short' }).format(new Date(at)),
    ]);
  });
});
