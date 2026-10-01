import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { FullScreen } from './full-screen';

describe('full screen', () => {
  afterEach(() =>
    Object.defineProperty(TestBed.inject(DOCUMENT), 'fullscreenElement', { configurable: true, value: null }),
  );

  /** Review focus 3: Esc is the browser's, not ours — the state follows its event, whoever left. */
  it('follows the browser in and out, Esc included', () => {
    const document = TestBed.inject(DOCUMENT);
    const fullScreen = TestBed.inject(FullScreen);
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: document.body });
    document.dispatchEvent(new Event('fullscreenchange'));
    const inside = fullScreen.active();
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: null });
    document.dispatchEvent(new Event('fullscreenchange'));

    expect([inside, fullScreen.active()]).toEqual([true, false]);
  });
});
