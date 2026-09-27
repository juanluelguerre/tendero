import { computed, DestroyRef, DOCUMENT, effect, inject, Injectable, signal } from '@angular/core';

/** What the person chose. `system` follows the operating system's preference, and is the default. */
export type ThemeChoice = 'system' | 'light' | 'dark';

/** What the page wears: the value of `data-theme` on the root element, which `tokens.css` reads. */
export type AppliedTheme = 'light' | 'dark';

export const ThemeChoices: readonly ThemeChoice[] = ['system', 'light', 'dark'];

/**
 * The key both apps keep the choice under. The inline script in each
 * `index.html` reads the same key before the first paint, so the page never
 * flashes the wrong theme while Angular starts; keep the two in step.
 */
export const ThemeStorageKey = 'tendero.theme';

/** The theme a choice resolves to, given whether the system prefers dark. */
export function appliedTheme(choice: ThemeChoice, systemPrefersDark: boolean): AppliedTheme {
  if (choice === 'system') return systemPrefersDark ? 'dark' : 'light';
  return choice;
}

/** A stored value, or `system` when there is none or it is not one of ours. */
export function themeChoiceOf(stored: string | null): ThemeChoice {
  return ThemeChoices.includes(stored as ThemeChoice) ? (stored as ThemeChoice) : 'system';
}

/**
 * Light or dark, chosen by the person, in both apps (the redesign's R-3). The
 * tokens have always had both themes and the contrast test holds both; what was
 * missing was the choice. It is a per-browser convenience: storage can be
 * missing or refuse, and the page still works — it just forgets.
 *
 * Shared because the difficulty is behaviour, not looks (ADR 0010): following
 * the system while it changes, applying before anything paints, and not
 * breaking where storage throws.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly document = inject(DOCUMENT);
  private readonly media = this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
  private readonly systemPrefersDark = signal(this.media?.matches ?? false);

  readonly choice = signal<ThemeChoice>(themeChoiceOf(this.read()));
  readonly applied = computed(() => appliedTheme(this.choice(), this.systemPrefersDark()));

  constructor() {
    const follow = (event: MediaQueryListEvent) => this.systemPrefersDark.set(event.matches);
    this.media?.addEventListener?.('change', follow);
    inject(DestroyRef).onDestroy(() => this.media?.removeEventListener?.('change', follow));

    effect(() => {
      this.document.documentElement.dataset['theme'] = this.applied();
    });
  }

  choose(choice: ThemeChoice): void {
    this.choice.set(choice);
    try {
      this.document.defaultView?.localStorage.setItem(ThemeStorageKey, choice);
    } catch {
      // Private windows and blocked storage: the choice holds for this page.
    }
  }

  private read(): string | null {
    try {
      return this.document.defaultView?.localStorage.getItem(ThemeStorageKey) ?? null;
    } catch {
      return null;
    }
  }
}
