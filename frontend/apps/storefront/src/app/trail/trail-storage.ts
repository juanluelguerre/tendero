import { Injectable } from '@angular/core';
import { EmptyTrail, readTrail, type TrailSnapshot } from './trail-rules';

/** Where a guest's trail lives: this browser, this origin, never the shop's server. */
export const TrailStorageKey = 'tendero.trail';

/**
 * The guest trail's one door to `localStorage` (R-8, H-3).
 *
 * **Storage that throws is an ordinary case, not an error**: a private window, a
 * blocked origin, a full quota. Every call is wrapped, reading answers an empty
 * trail and writing does nothing, so the row simply is not drawn — the same way
 * the listing's folded column already degrades.
 */
@Injectable({ providedIn: 'root' })
export class TrailStorage {
  read(now: Date): TrailSnapshot {
    try {
      return readTrail(localStorage.getItem(TrailStorageKey), now);
    } catch {
      return EmptyTrail;
    }
  }

  /** Switched off, only the switch is kept: the entries go with it. */
  write(trail: TrailSnapshot): void {
    try {
      localStorage.setItem(
        TrailStorageKey,
        JSON.stringify(trail.enabled ? trail : { enabled: false, views: [], searches: [] }),
      );
    } catch {
      // No storage: the trail lives for this page only, which is all a blocked browser allows.
    }
  }
}
