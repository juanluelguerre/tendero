import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { followsMedia } from '@tendero/shared-ui';
import { TinoService } from '../data-access/tino.service';
import { pageOf } from './tino-context';

/** From here the panel sits beside the page; below, it is a sheet over it. */
const DockedFrom = '(min-width: 64rem)';

/**
 * Tino's door (phase 12b; ADR 0047: the header's button, the search page's offer
 * and the panel's own close share it, so it is one root store). Whether the
 * panel is open, and whether Tino can be asked at all — asked of the server
 * once a visit, so the door says "not available" before anybody types a
 * question into it (the spec, *When no model answers*).
 *
 * Apart from the conversation on purpose: the shell needs only this, and the
 * conversation's code travels in the panel's deferred chunk.
 */
export const TinoDoorStore = signalStore(
  { providedIn: 'root' },
  withState({
    isOpen: false,
    /** A question the search page handed over, for the panel to ask as it opens. */
    handed: null as string | null,
  }),
  withProps(() => {
    const tino = inject(TinoService);
    return {
      _telemetry: inject(Telemetry),
      _router: inject(Router),
      /** Read once, when the shell first draws the door: what the server is read for is a resource (ADR 0051). */
      _status: extendResource(rxResource({ stream: () => tino.status() })),
      /** Whether there is room for the panel beside the page. */
      wide: followsMedia(DockedFrom),
    };
  }),
  withComputed((store) => {
    /** Null until the server has said; a shop that does not answer says no. */
    const available = computed<boolean | null>(() =>
      store._status.status() === 'error' ? false : (store._status.value()?.available ?? null),
    );
    return {
      available,
      /** The door is quiet and says so only once the server has said no: not while it has not answered yet. */
      unavailable: computed(() => available() === false),
      /** The panel is open beside the page: the shell gives it the room. */
      docked: computed(() => store.isOpen() && store.wide()),
    };
  }),
  withMethods((store) => {
    const open = (from: TrackedEvents['tino_opened']['from']) => {
      if (store.isOpen()) return;
      patchState(store, { isOpen: true });
      store._telemetry.trackEvent('tino_opened', { from, page: pageOf(store._router.url) });
      if (store.unavailable()) store._telemetry.trackEvent('tino_unavailable', { cause: 'not_available' });
    };
    return {
      open,
      close(): void {
        patchState(store, { isOpen: false });
      },
      toggle(): void {
        if (store.isOpen()) patchState(store, { isOpen: false });
        else open('header');
      },
      /** The search page's offer: open, and ask what was being searched for. */
      ask(question: string): void {
        patchState(store, { handed: question });
        open('search');
      },
      /** The panel took the question it was handed. */
      taken(): void {
        patchState(store, { handed: null });
      },
    };
  }),
);
