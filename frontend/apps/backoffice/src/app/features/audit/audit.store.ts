import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { translateObjectSignal, translateSignal } from '@jsverse/transloco';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { AuditOutcome } from '@tendero/shared-api';
import { CatalogService } from '../../data-access/catalog.service';
import { readStatus } from '../../read-status';
import type { FilterOption } from '../../worklist/filter-chips/filter-chips';

export type AuditFilter = AuditOutcome | null;

/** Twenty rows a page, as every worklist (R-7.2). */
export const PageSize = 20;

/** The chips, in the order a shopkeeper looks: what was refused first, then everything. */
const Filters: readonly AuditFilter[] = ['Denied', 'Failed', 'Allowed', null];

interface Counts {
  readonly byOutcome: ReadonlyMap<string, number>;
  readonly all: number;
}

/**
 * The audit log's state (ADR 0047): the outcome filter, the page, the one
 * payload open — they are JSON blobs, and two of them open at once is a wall —
 * and the log itself. It opens on what was denied: that is what the log is read for.
 */
export const AuditStore = signalStore(
  withState({ filter: 'Denied' as AuditFilter, page: 1, opened: null as string | null }),
  withProps((store) => {
    const catalog = inject(CatalogService);
    const log = rxResource({
      params: () => ({ filter: store.filter(), page: store.page() }),
      stream: ({ params }) => catalog.audit(params.filter, params.page, PageSize),
    });
    return {
      _log: log,
      ...readStatus(log),
      _labels: translateObjectSignal('audit.outcome'),
      _everything: translateSignal('audit.all'),
      /**
       * Every chip says its number, whichever is chosen, and all of them the log's:
       * it used to be the chosen one's, counted from the rows it had — which was
       * the page's once the log had more than one. Kept while the next page loads.
       */
      counts: linkedSignal<Counts | undefined, Counts | null>({
        source: () => {
          const answered = log.value()?.counts;
          if (!answered) return undefined;
          const byOutcome = new Map(answered.map((entry) => [entry.outcome as string, entry.count]));
          return { byOutcome, all: [...byOutcome.values()].reduce((sum, count) => sum + count, 0) };
        },
        computation: (latest, previous) => latest ?? previous?.value ?? null,
      }),
    };
  }),
  withComputed((store) => ({
    entries: computed(() => store._log.value()?.entries ?? []),
    total: computed(() => store._log.value()?.total ?? 0),
    filterOptions: computed<FilterOption[]>(() => {
      const labels = store._labels() as Record<string, string>;
      const counts = store.counts();
      return Filters.map((option) => ({
        key: option ?? 'all',
        label: option ? (labels[option] ?? option) : store._everything(),
        count: option ? (counts?.byOutcome.get(option) ?? null) : (counts?.all ?? null),
      }));
    }),
  })),
  withMethods((store) => ({
    /** A new filter starts at the first page, with every payload closed. */
    use(filter: AuditFilter): void {
      patchState(store, { filter, page: 1, opened: null });
    },
    go(page: number): void {
      patchState(store, { page, opened: null });
    },
    /** One payload open at a time. */
    toggle(id: string): void {
      patchState(store, (state) => ({ opened: state.opened === id ? null : id }));
    },
  })),
);
