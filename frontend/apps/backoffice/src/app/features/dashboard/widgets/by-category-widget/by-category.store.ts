import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';
import { valueOf } from '@tendero/shared-util';

/** A row of sales by category: named in the reader's language, or the rest together, or the lines with none. */
export interface CategoryRow {
  readonly key: string;
  readonly label: string;
  readonly units: number;
  readonly revenue: number;
}

/** A ranking is read in five: the rest are one row. */
const Shown = 5;

/**
 * By category's state (ADR 0047): the period's sales by the category each line
 * was sold in (R-9, D-7), as the server ranked them — the five that sold most by
 * name, the rest together, and the lines sold before the category was recorded
 * (or of a product with none) apart, never folded into a real one.
 */
export const ByCategoryStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    const culture = inject(CultureStore);
    return {
      mix: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.mix(params.period, at) }),
      _categories: refreshedResource({
        params: () => ({ culture: culture.active() }),
        stream: (params, at) => sources.categories(params.culture, at),
      }),
      _words: translateObjectSignal('dashboard.byCategory'),
    };
  }),
  withComputed((store) => ({
    rows: computed<CategoryRow[]>(() => {
      const mix = valueOf(store.mix);
      if (!mix) return [];
      const names = new Map(
        (valueOf(store._categories)?.items ?? []).map((category) => [category.code, category.name]),
      );
      const words = store._words() as Partial<Record<'others' | 'uncategorised', string>>;
      const known = mix.byCategory.flatMap((row) =>
        row.categoryCode === null ? [] : [{ ...row, code: row.categoryCode }],
      );
      const rest = known.slice(Shown);
      const none = mix.byCategory.find((row) => row.categoryCode === null);

      const rows: CategoryRow[] = known.slice(0, Shown).map((row) => ({
        key: row.code,
        label: names.get(row.code) ?? row.code,
        units: row.units,
        revenue: row.revenue,
      }));
      if (rest.length > 0) {
        rows.push({
          key: 'others',
          label: words.others ?? '',
          units: rest.reduce((sum, row) => sum + row.units, 0),
          revenue: rest.reduce((sum, row) => sum + row.revenue, 0),
        });
      }
      if (none)
        rows.push({ key: 'uncategorised', label: words.uncategorised ?? '', units: none.units, revenue: none.revenue });
      return rows;
    }),
  })),
);
