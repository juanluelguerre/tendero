import type { Signal } from '@angular/core';
import { inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import type { DashboardPeriod } from '../../data-access/ordering.service';

export type PeriodQuery = 'day' | 'week' | 'month';

const ByQuery: Readonly<Record<PeriodQuery, DashboardPeriod>> = { day: 'Day', week: 'Week', month: 'Month' };

/**
 * The period a URL names (spec: "it is in the URL, so a monitor keeps its
 * period"). Read in any case, as ADR 0043 reads a closed set; anything else is
 * the default week, because a link to a dashboard should open one.
 */
export function periodFromQuery(value: string | null): DashboardPeriod {
  return ByQuery[(value ?? '').toLowerCase() as PeriodQuery] ?? 'Week';
}

export function queryOfPeriod(period: DashboardPeriod): PeriodQuery {
  return period.toLowerCase() as PeriodQuery;
}

/** The dashboard's period, from the current route's query: the shell and every tab read the same one. */
export function injectDashboardPeriod(): Signal<DashboardPeriod> {
  return toSignal(inject(ActivatedRoute).queryParamMap.pipe(map((query) => periodFromQuery(query.get('period')))), {
    requireSync: true,
  });
}
