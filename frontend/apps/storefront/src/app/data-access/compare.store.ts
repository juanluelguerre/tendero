import { computed, Injectable, signal } from '@angular/core';

/** A product picked for comparison: its code is the key, the name is for the tray. */
export interface Compared {
  readonly code: string;
  readonly name: string;
}

const KEY = 'tendero.compare';

/** The server compares four at most (`ProductSpecsValidator`); the tray says so before it asks. */
export const MAX_COMPARED = 4;

/**
 * The products a shopper has picked to compare (P10-7).
 *
 * Kept in the browser, and only as a convenience: the comparison itself is the
 * URL (`/compare?codes=…`), so it can be shared and reloaded, and a storage
 * that is blocked or cleared costs the tray and nothing else.
 */
@Injectable({ providedIn: 'root' })
export class CompareStore {
  private readonly picked = signal<readonly Compared[]>(read());

  readonly items = this.picked.asReadonly();
  readonly full = computed(() => this.picked().length >= MAX_COMPARED);
  readonly codes = computed(() => this.picked().map((item) => item.code));

  has(code: string): boolean {
    return this.picked().some((item) => item.code === code);
  }

  /** Adds or removes; adding to a full tray does nothing, and the toggle is disabled then. */
  toggle(item: Compared): void {
    const current = this.picked();
    const next = this.has(item.code)
      ? current.filter((existing) => existing.code !== item.code)
      : current.length < MAX_COMPARED
        ? [...current, item]
        : current;
    this.set(next);
  }

  clear(): void {
    this.set([]);
  }

  private set(items: readonly Compared[]): void {
    this.picked.set(items);
    try {
      localStorage.setItem(KEY, JSON.stringify(items));
    } catch {
      // Storage blocked or full: the tray lives for this page, which is enough.
    }
  }
}

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
