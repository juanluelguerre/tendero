import { TestBed } from '@angular/core/testing';
import { appliedTheme, ThemeStorageKey, ThemeStore, themeChoiceOf } from './theme';

describe('the theme choice', () => {
  it('follows the system only when asked to', () => {
    expect(appliedTheme('system', true)).toBe('dark');
    expect(appliedTheme('system', false)).toBe('light');
    expect(appliedTheme('light', true)).toBe('light');
    expect(appliedTheme('dark', false)).toBe('dark');
  });

  it('reads anything it did not write as the default', () => {
    expect(themeChoiceOf('dark')).toBe('dark');
    expect(themeChoiceOf(null)).toBe('system');
    expect(themeChoiceOf('sepia')).toBe('system');
  });

  describe('the store', () => {
    beforeEach(() => localStorage.clear());

    it('applies the choice to the page and remembers it', () => {
      const store = TestBed.inject(ThemeStore);

      store.choose('dark');
      TestBed.tick();

      expect(document.documentElement.dataset['theme']).toBe('dark');
      expect(localStorage.getItem(ThemeStorageKey)).toBe('dark');

      store.choose('light');
      TestBed.tick();
      expect(document.documentElement.dataset['theme']).toBe('light');
    });

    it('starts from what was remembered', () => {
      localStorage.setItem(ThemeStorageKey, 'dark');

      expect(TestBed.inject(ThemeStore).choice()).toBe('dark');
    });

    it('keeps working when storage refuses', () => {
      const refuse = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new DOMException('blocked', 'SecurityError');
      });
      const store = TestBed.inject(ThemeStore);

      store.choose('dark');
      TestBed.tick();

      expect(store.applied()).toBe('dark');
      refuse.mockRestore();
    });
  });
});
