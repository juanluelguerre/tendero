import { afterEach, describe, expect, it, vi } from 'vitest';
import { followsMedia } from './follows-media';

/** Which of two elements exists is asked of the window, and followed as it changes. */
describe('following a media query', () => {
  const original = window.matchMedia;
  afterEach(() => (window.matchMedia = original));

  it('says what the window says, and follows it as it changes', () => {
    let heard: ((event: MediaQueryListEvent) => void) | undefined;
    window.matchMedia = vi.fn().mockReturnValue({
      matches: true,
      addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => (heard = listener),
    } as unknown as MediaQueryList);

    const wide = followsMedia('(min-width: 64rem)');
    expect(wide()).toBe(true);

    heard?.({ matches: false } as MediaQueryListEvent);
    expect(wide()).toBe(false);
  });

  it('does not match where there is no window to ask', () => {
    window.matchMedia = undefined as unknown as typeof window.matchMedia;

    expect(followsMedia('(min-width: 64rem)')()).toBe(false);
  });
});
