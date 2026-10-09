import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { type VisualSearchPhase, VisualSearchStore } from '../visual-search.store';
import { VisualSearchPanel } from './visual-search-panel';

const words = {
  visualSearch: {
    title: 'Search with a picture',
    drop: 'Drop a picture here',
    dropIt: 'Drop it',
    orPaste: 'or paste one',
    choose: 'Choose a picture',
    takePhoto: 'Take a photo',
    looking: 'Looking for things like it',
    promise: 'Your picture is not kept.',
    refused: 'That file is not a picture we can read.',
    tooMany: 'Too many pictures in a minute.',
    down: 'Searching by picture is not answering.',
  },
};

/** The panel where a picture is given: every way of giving one, and what it says when the shop does not take it. */
describe('the panel to search with a picture', () => {
  let fixture: ComponentFixture<VisualSearchPanel>;
  const phase = signal<VisualSearchPhase>('idle');
  const store = {
    phase,
    looking: () => phase() === 'looking',
    give: vi.fn(),
    close: vi.fn(),
  };
  const picture = new File(['a picture'], 'lamp.png', { type: 'image/png' });

  beforeEach(async () => {
    phase.set('idle');
    store.give.mockClear();
    store.close.mockClear();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: words },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [{ provide: VisualSearchStore, useValue: store }],
    });
    fixture = TestBed.createComponent(VisualSearchPanel);
    await fixture.whenStable();
  });

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const find = <T extends Element>(selector: string) =>
    (fixture.nativeElement as HTMLElement).querySelector<T & HTMLElement>(selector)!;

  /** The one sentence that is always there. */
  it('always says the picture is not kept', async () => {
    expect(text()).toContain('Your picture is not kept.');

    phase.set('looking');
    await fixture.whenStable();

    expect(text()).toContain('Your picture is not kept.');
  });

  it('gives the store a picture that was chosen, and empties the picker so the same file can be chosen again', () => {
    const input = find<HTMLInputElement>('[data-visual-search-file]');
    Object.defineProperty(input, 'files', { value: [picture], configurable: true });

    input.dispatchEvent(new Event('change'));

    expect(store.give).toHaveBeenCalledWith({ picture, how: 'chosen' });
    expect(input.value).toBe('');
  });

  it('takes only what the shop reads from the picker', () => {
    expect(find<HTMLInputElement>('[data-visual-search-file]').accept).toBe('image/jpeg,image/png,image/webp');
  });

  it('gives the store a picture that was dropped', () => {
    const drop = new Event('drop') as DragEvent;
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [picture] } });

    find('[data-visual-search-drop]').dispatchEvent(drop);

    expect(store.give).toHaveBeenCalledWith({ picture, how: 'dropped' });
  });

  /** Without this the browser opens the file it was handed, and the shop is gone from the tab. */
  it('keeps the browser from opening a picture dropped on it', () => {
    const drop = new Event('drop', { cancelable: true }) as DragEvent;
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [picture] } });
    const over = new Event('dragover', { cancelable: true });

    find('[data-visual-search-drop]').dispatchEvent(over);
    find('[data-visual-search-drop]').dispatchEvent(drop);

    expect([over.defaultPrevented, drop.defaultPrevented]).toEqual([true, true]);
  });

  it('gives the store a picture that was pasted, and leaves pasted words alone', () => {
    const words = new Event('paste') as ClipboardEvent;
    Object.defineProperty(words, 'clipboardData', {
      value: { files: [new File(['x'], 'note.txt', { type: 'text/plain' })] },
    });
    document.dispatchEvent(words);
    expect(store.give).not.toHaveBeenCalled();

    const pasted = new Event('paste') as ClipboardEvent;
    Object.defineProperty(pasted, 'clipboardData', { value: { files: [picture] } });
    document.dispatchEvent(pasted);

    expect(store.give).toHaveBeenCalledWith({ picture, how: 'pasted' });
  });

  it('shows that it is looking instead of the ways to give a picture', async () => {
    phase.set('looking');
    await fixture.whenStable();

    expect(text()).toContain('Looking for things like it');
    expect(find('[data-visual-search-choose]')).toBeNull();
  });

  it.each([
    ['refused', 'That file is not a picture we can read.'],
    ['tooMany', 'Too many pictures in a minute.'],
    ['down', 'Searching by picture is not answering.'],
  ] as const)(
    'says why when the shop does not answer (%s), with the way to try another still there',
    async (state, said) => {
      phase.set(state);
      await fixture.whenStable();

      expect(text()).toContain(said);
      expect(find('[data-visual-search-choose]')).not.toBeNull();
    },
  );

  it('closes on Escape and on a press outside it, and not on a press inside or on the camera that opened it', () => {
    find('section').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(store.close).toHaveBeenCalledTimes(1);

    find('section').dispatchEvent(new Event('pointerdown', { bubbles: true }));
    expect(store.close).toHaveBeenCalledTimes(1);

    const door = document.createElement('button');
    door.setAttribute('data-visual-search-door', '');
    document.body.append(door);
    door.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    expect(store.close).toHaveBeenCalledTimes(1);
    door.remove();

    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    expect(store.close).toHaveBeenCalledTimes(2);
  });
});
