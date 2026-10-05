import { signal, type Signal } from '@angular/core';

/**
 * Whether a media query matches, followed as the window changes — for what a
 * template cannot ask with a breakpoint: which of two elements exists (a column
 * beside the page, or a modal sheet over it). Where there is no such thing to
 * ask, it does not match.
 */
export function followsMedia(query: string): Signal<boolean> {
  const list = typeof window.matchMedia === 'function' ? window.matchMedia(query) : null;
  const matches = signal(list?.matches ?? false);
  list?.addEventListener('change', (event) => matches.set(event.matches));
  return matches.asReadonly();
}
