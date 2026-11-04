import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { HeldOrder } from '@tendero/shared-api';
import { ChannelsService } from '../../../data-access/channels.service';

/**
 * The orders a channel took and charged for that could not be received (P16-29), as
 * state of the band that shows them (ADR 0047): read once with the page. A failed read
 * is no orders — the band is a warning beside the list, never a reason to break it.
 */
export const HeldOrdersStore = signalStore(
  withProps(() => {
    const channels = inject(ChannelsService);
    return { _held: extendResource(rxResource({ stream: () => channels.heldOrders() })) };
  }),
  withComputed(({ _held }) => ({
    orders: computed<readonly HeldOrder[]>(() => _held.value()?.orders ?? []),
  })),
);
