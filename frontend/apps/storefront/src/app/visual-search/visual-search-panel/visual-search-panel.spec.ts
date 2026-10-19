import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PictureSheetStore } from '../picture-sheet/picture-sheet.store';
import { VisualSearchStore } from '../visual-search.store';
import { VisualSearchPanel } from './visual-search-panel';

const words = {
  visualSearch: {
    title: 'Search with a picture',
    drop: 'Drop a picture here',
    dropIt: 'Drop it',
    orPaste: 'or paste one',
    choose: 'Choose a picture',
    takePhoto: 'Take a photo',
    promise: 'Your picture is not kept.',
  },
};

/** The panel where a picture is given: every way of giving one, each handed to the picture sheet. */
describe('the panel to search with a picture', () => {
  let fixture: ComponentFixture<VisualSearchPanel>;
  const store = { close: vi.fn() };
  const sheet = { start: vi.fn() };
  const picture = new File(['a picture'], 'lamp.png', { type: 'image/png' });

  beforeEach(async () => {
    sheet.start.mockClear();
    store.close.mockClear();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: words },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [
        { provide: VisualSearchStore, useValue: store },
        { provide: PictureSheetStore, useValue: sheet },
      ],
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
  });

  it('hands the sheet a picture that was chosen, and empties the picker so the same file can be chosen again', () => {
    const input = find<HTMLInputElement>('[data-visual-search-file]');
    Object.defineProperty(input, 'files', { value: [picture], configurable: true });

    input.dispatchEvent(new Event('change'));

    expect(sheet.start).toHaveBeenCalledWith(picture, 'chosen');
    expect(store.close).toHaveBeenCalled();
    expect(input.value).toBe('');
  });

  it('takes only what the shop reads from the picker', () => {
    expect(find<HTMLInputElement>('[data-visual-search-file]').accept).toBe('image/jpeg,image/png,image/webp');
  });

  it('hands the sheet a picture that was dropped', () => {
    const drop = new Event('drop') as DragEvent;
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [picture] } });

    find('[data-visual-search-drop]').dispatchEvent(drop);

    expect(sheet.start).toHaveBeenCalledWith(picture, 'dropped');
    expect(store.close).toHaveBeenCalled();
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

  it('hands the sheet a picture that was pasted, and leaves pasted words alone', () => {
    const words = new Event('paste') as ClipboardEvent;
    Object.defineProperty(words, 'clipboardData', {
      value: { files: [new File(['x'], 'note.txt', { type: 'text/plain' })] },
    });
    document.dispatchEvent(words);
    expect(sheet.start).not.toHaveBeenCalled();

    const pasted = new Event('paste') as ClipboardEvent;
    Object.defineProperty(pasted, 'clipboardData', { value: { files: [picture] } });
    document.dispatchEvent(pasted);

    expect(sheet.start).toHaveBeenCalledWith(picture, 'pasted');
    expect(store.close).toHaveBeenCalled();
  });

  it('opens the phone’s own camera app where the page cannot show the camera', () => {
    const app = find<HTMLInputElement>('input[capture]');
    const click = vi.spyOn(app, 'click').mockImplementation(() => undefined);
    const devices = Object.getOwnPropertyDescriptor(navigator, 'mediaDevices');
    Object.defineProperty(navigator, 'mediaDevices', { value: undefined, configurable: true });
    try {
      find('[data-visual-search-camera]').click();
    } finally {
      if (devices) Object.defineProperty(navigator, 'mediaDevices', devices);
      else delete (navigator as { mediaDevices?: unknown }).mediaDevices;
    }

    expect(click).toHaveBeenCalled();
    expect(find('storefront-viewfinder')).toBeNull();
  });

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
