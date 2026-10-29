import { inject, signal } from '@angular/core';
import { form, required } from '@angular/forms/signals';
import { TranslocoService } from '@jsverse/transloco';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { refusalSentence } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';
import { ExportsService } from '../../../data-access/exports.service';

/** A span of the shop's days, as date inputs hold it: ISO dates, both included. */
export interface OrdersSpan {
  from: string;
  to: string;
}

/** The month before the one `today` is in, whole: what an accountant asks for at the start of a month. */
export function lastMonth(today: Date): OrdersSpan {
  const first = new Date(today.getFullYear(), today.getMonth() - 1, 1);
  const last = new Date(today.getFullYear(), today.getMonth(), 0);
  return { from: day(first), to: day(last) };
}

function day(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

interface OrdersExportState {
  readonly downloading: boolean;
  /** Why the last download did not happen, in the server's words when it gave some; null otherwise. */
  readonly problem: string | null;
}

/**
 * Orders for the accountant (phase 16, E-4; ADR 0047): the span being chosen and
 * the download. The span is a Signal Form (ADR 0051) over a writable signal in the
 * store's props, so the date inputs bind with `[formField]`; the store asks the
 * server, and the panel hands the file to the browser.
 */
export const OrdersExportStore = signalStore(
  withState<OrdersExportState>({ downloading: false, problem: null }),
  withProps(() => {
    const span = signal<OrdersSpan>(lastMonth(new Date()));
    return {
      span,
      spanForm: form(span, (path) => {
        required(path.from);
        required(path.to);
      }),
      _exports: inject(ExportsService),
      _transloco: inject(TranslocoService),
    };
  }),
  withMethods((store) => ({
    /** The span's sales as the accountant's spreadsheet, or null — with a sentence — when it could not be had. */
    async download(): Promise<Blob | null> {
      const { from, to } = store.span();
      if (!from || !to || to < from) {
        patchState(store, { problem: store._transloco.translate('orders.export.badSpan') });
        return null;
      }

      patchState(store, { downloading: true, problem: null });
      try {
        const file = await firstValueFrom(store._exports.orders(from, to));
        patchState(store, { downloading: false });
        return file;
      } catch (failure) {
        patchState(store, {
          downloading: false,
          problem:
            refusalSentence((failure as { error?: unknown }).error) ||
            store._transloco.translate('orders.export.failed'),
        });
        return null;
      }
    },
  })),
);
