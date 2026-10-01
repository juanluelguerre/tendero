import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { valueOf } from '@tendero/shared-util';

/**
 * Search status's state (ADR 0047): each stage of search — words, meaning,
 * reranking — its state and since when, from the checks the API keeps (R-9, D-7).
 * A search is whole when no stage that is on is down; a stage switched off is a
 * choice of the process (invariant 8), not an alarm.
 */
export const SearchStatusStore = signalStore(
  withProps(() => {
    const sources = inject(DashboardSources);
    return { status: refreshedResource({ params: () => ({}), stream: (_, at) => sources.searchStatus(at) }) };
  }),
  withComputed((store) => {
    const stages = computed(() => valueOf(store.status)?.stages ?? []);
    const down = computed(() => stages().filter((stage) => stage.state === 'Down'));
    return { stages, down, whole: computed(() => down().length === 0) };
  }),
);
