import { computed } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';

/** A product picked for comparison: its code is the key, the name is for the tray. */
export interface Compared {
  readonly code: string;
  readonly name: string;
}

const KEY = 'tendero.compare';

/** The server compares four at most (`ProductSpecsValidator`); the tray says so before it asks. */
export const MAX_COMPARED = 4;

/**
 * The products a shopper has picked to compare (P10-7; ADR 0047).
 *
 * Kept in the browser, and only as a convenience: the comparison itself is the
 * URL (`/compare?codes=…`), so it can be shared and reloaded, and a storage
 * that is blocked or cleared costs the tray and nothing else.
 */
export const CompareStore = signalStore(
  { providedIn: 'root' },
  withState(() => ({ items: read() })),
  withComputed(({ items }) => ({
    full: computed(() => items().length >= MAX_COMPARED),
    codes: computed(() => items().map((item) => item.code)),
  })),
  withMethods((store) => {
    const has = (code: string): boolean => store.items().some((item) => item.code === code);

    const set = (items: readonly Compared[]): void => {
      patchState(store, { items });
      try {
        localStorage.setItem(KEY, JSON.stringify(items));
      } catch {
        // Storage blocked or full: the tray lives for this page, which is enough.
      }
    };

    return {
      has,
      /** Adds or removes; adding to a full tray does nothing, and the toggle is disabled then. */
      toggle(item: Compared): void {
        const current = store.items();
        set(
          has(item.code)
            ? current.filter((existing) => existing.code !== item.code)
            : current.length < MAX_COMPARED
              ? [...current, item]
              : current,
        );
      },
      clear(): void {
        set([]);
      },
    };
  }),
);

function read(): readonly Compared[] {
  try {
    const stored = JSON.parse(localStorage.getItem(KEY) ?? '[]') as unknown;
    return Array.isArray(stored)
      ? stored
          .filter((item): item is Compared => typeof item?.code === 'string' && typeof item?.name === 'string')
          .slice(0, MAX_COMPARED)
      : [];
  } catch {
    return [];
  }
}
