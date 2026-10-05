import { computed, inject, signal, type Signal } from '@angular/core';
import { Router } from '@angular/router';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { catchError, of, pipe, switchMap, tap } from 'rxjs';
import { TinoService } from '../data-access/tino.service';
import { pageOf } from './tino-context';

/** From here the panel sits beside the page; below, it is a sheet over it. */
const DockedFrom = '(min-width: 64rem)';

/** Whether a media query matches, followed as the window changes; where there is no such thing to ask, it does not. */
function follows(query: string): Signal<boolean> {
  const list = typeof window.matchMedia === 'function' ? window.matchMedia(query) : null;
  const matches = signal(list?.matches ?? false);
  list?.addEventListener('change', (event) => matches.set(event.matches));
  return matches.asReadonly();
}

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
    /** Null until the server has said; a shop that does not answer says no. */
    available: null as boolean | null,
    /** A question the search page handed over, for the panel to ask as it opens. */
    handed: null as string | null,
  }),
  withProps(() => ({
    _tino: inject(TinoService),
    _telemetry: inject(Telemetry),
    _router: inject(Router),
    /** Whether there is room for the panel beside the page. */
    wide: follows(DockedFrom),
  })),
  withComputed((store) => ({
    /** The panel is open beside the page: the shell gives it the room. */
    docked: computed(() => store.isOpen() && store.wide()),
    /** The door is quiet and says so only once the server has said no: not while it has not answered yet. */
    unavailable: computed(() => store.available() === false),
  })),
  withMethods((store) => {
    const open = (from: TrackedEvents['tino_opened']['from']) => {
      if (store.isOpen()) return;
      patchState(store, { isOpen: true });
      store._telemetry.trackEvent('tino_opened', { from, page: pageOf(store._router.url) });
      if (store.unavailable()) store._telemetry.trackEvent('tino_unavailable', { cause: 'not_available' });
    };
    return {
      /** Asked once, when the shell is drawn. */
      check: rxMethod<void>(
        pipe(
          switchMap(() => store._tino.status().pipe(catchError(() => of({ available: false })))),
          tap((status) => patchState(store, { available: status.available })),
        ),
      ),
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
