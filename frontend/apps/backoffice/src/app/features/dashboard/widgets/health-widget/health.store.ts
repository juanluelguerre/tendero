import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';
import { valueOf } from '@tendero/shared-util';

/** One rate against its target: its standing the server decided, and what it counted. */
export interface HealthRate {
  readonly key: 'cancellation' | 'defect' | 'lateShipment';
  /** Null when there was nothing to divide by: *no data*, never 0 %. */
  readonly rate: number | null;
  readonly target: number;
  readonly standing: string | null;
  readonly count: number;
  /** What was left out of the rate and is said apart — orders shipped before their moments were recorded. */
  readonly apart: number;
}

/**
 * Health's state (ADR 0047): the period's operations — the cancellation, order
 * defect and late shipment rates, each against its target (R-9, D-7), whose
 * standing the server decided.
 */
export const HealthStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      operations: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.operations(params.period, at) }),
    };
  }),
  withComputed((store) => ({
    rates: computed<HealthRate[]>(() => {
      const ops = valueOf(store.operations);
      if (!ops) return [];
      return [
        { key: 'cancellation', rate: ops.cancellationRate, target: ops.cancellationTarget, standing: ops.cancellationStanding, count: ops.cancelled, apart: 0 },
        { key: 'defect', rate: ops.defectRate ?? null, target: ops.defectTarget, standing: ops.defectStanding ?? null, count: ops.defects, apart: 0 },
        {
          key: 'lateShipment',
          rate: ops.lateShipmentRate ?? null,
          target: ops.lateShipmentTarget,
          standing: ops.lateShipmentStanding ?? null,
          count: ops.lateShipments,
          apart: ops.shippedBeforeMoments,
        },
      ];
    }),
  })),
);
