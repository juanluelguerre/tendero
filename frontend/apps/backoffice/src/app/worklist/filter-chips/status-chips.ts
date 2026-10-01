import { linkedSignal, type Signal } from '@angular/core';
import type { FilterOption } from './filter-chips';

/** How many a list holds per status, as the list endpoints answer it. */
export interface StatusCount {
  readonly status: string;
  readonly count: number;
}

/**
 * The counts a list answered last, kept while the next answer is on its way.
 *
 * A list asked again (another filter, a search being typed) has no counts for a
 * moment; drawn as that moment, every count blanks and comes back, and a row of
 * chips that depends on them moves under the pointer. Null only before the
 * first answer.
 */
export function keptCounts(
  counts: () => readonly StatusCount[] | undefined,
): Signal<ReadonlyMap<string, number> | null> {
  return linkedSignal<ReadonlyMap<string, number> | undefined, ReadonlyMap<string, number> | null>({
    source: () => {
      const answered = counts();
      return answered && new Map(answered.map((entry) => [entry.status, entry.count]));
    },
    computation: (latest, previous) => latest ?? previous?.value ?? null,
  });
}

/**
 * One chip per status, always the same ones in the same order, then *All*.
 *
 * The set is fixed on purpose. A chip that appears when its status has rows
 * and goes when it has none changes the row's width with every answer, and
 * the search box beside it grows and shrinks with it — the stutter the
 * redesign's second walkthrough found on Orders, whose chips came and went,
 * and not on Products, whose did not. An empty status says 0, which is also
 * information: nothing is waiting there.
 */
export function statusChips(
  statuses: readonly string[],
  labels: Readonly<Record<string, string>>,
  counts: ReadonlyMap<string, number> | null,
): FilterOption[] {
  const total = counts ? [...counts.values()].reduce((sum, count) => sum + count, 0) : null;
  return [
    ...statuses.map((status) => ({
      key: status,
      label: labels[status] ?? status,
      count: counts ? (counts.get(status) ?? 0) : null,
    })),
    { key: 'all', label: labels['all'] ?? 'all', count: total },
  ];
}

/**
 * The counts for a list the server answers whole, counted here: one entry per
 * key the items fall under, in the shape the list endpoints answer, so
 * `keptCounts` and `statusChips` take it as they take theirs. A key no item
 * falls under is absent, and `statusChips` says 0 for it.
 */
export function countBy<T>(items: readonly T[], keyOf: (item: T) => string): StatusCount[] {
  const counts = new Map<string, number>();
  for (const item of items) {
    const key = keyOf(item);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  return [...counts].map(([status, count]) => ({ status, count }));
}
