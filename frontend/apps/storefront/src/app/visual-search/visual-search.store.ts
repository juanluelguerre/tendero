import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { VisualMatch } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { VisualSearchService } from '../data-access/visual-search.service';

/** How a picture was given: the tracking plan's own words. */
export type PictureGiven = TrackedEvents['search_by_image_submitted']['how'];

/**
 * Searching the shop with a picture (phase 13a; ADR 0047: the header's camera,
 * its panel and the page of one list share it, so it is one root store).
 *
 * Whether the shop can be asked is read once a visit, so the camera is drawn
 * only where a picture would be answered: a shop without visual search has
 * exactly the search box it always had (invariant 8). What a picture given is
 * answered with — its objects, a block each — is the picture sheet's
 * (`PictureSheetStore`), which reads this store and is never read by it.
 *
 * **The page's picture stays in the browser's memory**: an object URL, revoked
 * when another list replaces it.
 */
export const VisualSearchStore = signalStore(
  { providedIn: 'root' },
  withState({
    isOpen: false,
    /** The picture the page's list answers — an object's crop — as a URL only this page can read. */
    preview: null as string | null,
    /** The page's one list; null until a block's list was opened in this visit. */
    matches: null as readonly VisualMatch[] | null,
    /** Which object of the picture the list is for, from 1; the tracking plan counts a product followed by it. */
    block: 0,
  }),
  withProps(() => {
    const service = inject(VisualSearchService);
    return {
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
    /** Whether a picture's objects can be found; without it, what is sent is one object, searched whole (CS-9). */
    objects: computed(() => store._status.status() !== 'error' && store._status.value()?.objects === true),
  })),
  withMethods((store) => ({
    open(): void {
      patchState(store, { isOpen: true });
    },
    close(): void {
      patchState(store, { isOpen: false });
    },
    toggle(): void {
      patchState(store, { isOpen: !store.isOpen() });
    },

    /** One block's whole list, on the page of its own (*See all*), with the crop it answers. */
    show(matches: readonly VisualMatch[], picture: Blob, block: number): void {
      const before = store.preview();
      if (before) URL.revokeObjectURL(before);
      patchState(store, { isOpen: false, matches, block, preview: URL.createObjectURL(picture) });
      void store._router.navigate(['/', store._culture.active(), 'search', 'picture']);
    },

    /** A product was opened from an answer to a picture, from its place in it and the object it answered. */
    followed(position: number, block = store.block()): void {
      store._telemetry.trackEvent('search_by_image_result_followed', { position, block });
    },
  })),
);
