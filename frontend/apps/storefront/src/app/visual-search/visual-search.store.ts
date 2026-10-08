import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { extendResource } from '@ngrx/signals/resource';
import type { VisualMatch } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { catchError, EMPTY, from, pipe, switchMap, tap } from 'rxjs';
import { VisualSearchService } from '../data-access/visual-search.service';
import { shrinkPicture } from './shrink-picture';

/** How a picture was given: the tracking plan's own words. */
export type PictureGiven = TrackedEvents['search_by_image_submitted']['how'];

/**
 * Where a search with a picture is: nothing asked, looking, or one of the three
 * ways it does not answer — the file is not a picture the shop takes, too many
 * were asked in a minute, or the shop is not answering.
 */
export type VisualSearchPhase = 'idle' | 'looking' | 'refused' | 'tooMany' | 'down';

/**
 * Searching the shop with a picture (phase 13a; ADR 0047: the header's camera,
 * its panel and the page of results share it, so it is one root store).
 *
 * Whether the shop can be asked is read once a visit, so the camera is drawn
 * only where a picture would be answered: a shop without visual search has
 * exactly the search box it always had (invariant 8).
 *
 * **The picture stays in the browser's memory**: an object URL for the page of
 * results to show beside them, revoked when another picture replaces it. What
 * is sent is a smaller copy, and nothing of it is kept by the shop.
 */
export const VisualSearchStore = signalStore(
  { providedIn: 'root' },
  withState({
    isOpen: false,
    phase: 'idle' as VisualSearchPhase,
    /** The picture last given, as a URL only this page can read. */
    preview: null as string | null,
    /** What the shop answered; null until a picture has been answered in this visit. */
    matches: null as readonly VisualMatch[] | null,
  }),
  withProps(() => {
    const service = inject(VisualSearchService);
    return {
      _service: service,
      _telemetry: inject(Telemetry),
      _router: inject(Router),
      _culture: inject(CultureStore),
      /** Read once, when the shell first draws the search box: what the server is read for is a resource (ADR 0051). */
      _status: extendResource(rxResource({ stream: () => service.status() })),
    };
  }),
  withComputed((store) => ({
    /** Only a yes draws the camera: not yet answered, an error and a no all leave the box as it was. */
    available: computed(() => store._status.status() !== 'error' && store._status.value()?.available === true),
    looking: computed(() => store.phase() === 'looking'),
  })),
  withMethods((store) => {
    const submitted = (
      how: PictureGiven,
      outcome: TrackedEvents['search_by_image_submitted']['outcome'],
      results: number,
    ) => store._telemetry.trackEvent('search_by_image_submitted', { how, outcome, results });

    return {
      open(): void {
        patchState(store, { isOpen: true, phase: store.looking() ? 'looking' : 'idle' });
      },
      close(): void {
        patchState(store, { isOpen: false });
      },
      toggle(): void {
        if (store.isOpen()) patchState(store, { isOpen: false });
        else patchState(store, { isOpen: true, phase: store.looking() ? 'looking' : 'idle' });
      },

      /**
       * A picture to search with. Only the latest one's answer counts
       * (`switchMap`): giving another while the first is looked for abandons the first.
       */
      give: rxMethod<{ readonly picture: Blob; readonly how: PictureGiven }>(
        pipe(
          tap(({ picture }) => {
            const before = store.preview();
            if (before) URL.revokeObjectURL(before);
            patchState(store, { phase: 'looking', preview: URL.createObjectURL(picture) });
          }),
          switchMap(({ picture, how }) =>
            from(shrinkPicture(picture)).pipe(
              switchMap((small) => store._service.byPicture(small, store._culture.active())),
              tap((answer) => {
                patchState(store, { phase: 'idle', isOpen: false, matches: answer.items });
                submitted(how, 'answered', answer.items.length);
                void store._router.navigate(['/', store._culture.active(), 'search', 'picture']);
              }),
              // Caught inside the inner stream, so one picture that fails does not end the method for the next.
              catchError((failure: unknown) => {
                const status = failure instanceof HttpErrorResponse ? failure.status : 0;
                // The file itself: not a picture, too heavy, too large. Said beside the picture, so another can be tried.
                const phase: VisualSearchPhase =
                  status === 400 || status === 413 || status === 415 ? 'refused' : status === 429 ? 'tooMany' : 'down';
                patchState(store, { phase });
                submitted(how, phase === 'refused' ? 'refused' : phase === 'tooMany' ? 'too_many' : 'down', 0);
                return EMPTY;
              }),
            ),
          ),
        ),
      ),

      /** A product of the answer was opened, from its place in it. */
      followed(position: number): void {
        store._telemetry.trackEvent('search_by_image_result_followed', { position });
      },
    };
  }),
);
