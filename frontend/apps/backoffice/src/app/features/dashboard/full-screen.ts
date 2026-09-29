import { DestroyRef, DOCUMENT, inject, Injectable, signal } from '@angular/core';

/**
 * The browser's Fullscreen API as a signal (R-9): whether the page is on a
 * wall, whoever put it there or took it off — the button, or Esc, which is the
 * browser's and never reaches the page as a key.
 */
@Injectable({ providedIn: 'root' })
export class FullScreen {
  private readonly document = inject(DOCUMENT);
  private readonly inside = signal(this.document.fullscreenElement != null);

  readonly supported: boolean = this.document.fullscreenEnabled ?? false;
  readonly active = this.inside.asReadonly();

  constructor() {
    const follow = () => this.inside.set(this.document.fullscreenElement != null);
    this.document.addEventListener('fullscreenchange', follow);
    inject(DestroyRef).onDestroy(() => this.document.removeEventListener('fullscreenchange', follow));
  }

  enter(element: Element): Promise<void> {
    return element.requestFullscreen();
  }

  leave(): Promise<void> {
    return this.document.fullscreenElement ? this.document.exitFullscreen() : Promise.resolve();
  }
}
