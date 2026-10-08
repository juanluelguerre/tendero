import { Component, ElementRef, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Icon, Notice } from '@tendero/shared-ui';
import { type PictureGiven, VisualSearchStore } from '../visual-search.store';

/**
 * The panel under the search box where a picture is given (phase 13a; the
 * mock-up, `storefront-visual-search`): dropped, chosen, pasted, or — on a
 * phone — taken with the camera. It says the one thing that is always true of
 * it, that the picture is not kept, and when the shop does not take the file it
 * says so here, where another can be tried.
 *
 * It closes on Escape and on a click outside it. Nothing here is the page's
 * accent action: the panel is a way to ask, not the thing asked for.
 */
@Component({
  selector: 'storefront-visual-search-panel',
  imports: [TranslocoDirective, Icon, Notice],
  templateUrl: './visual-search-panel.html',
  host: {
    class: 'block',
    // On the page, not on the panel: the panel may not hold the focus when Escape is pressed.
    '(document:keydown.escape)': 'store.close()',
    '(document:pointerdown)': 'outside($event)',
    '(document:paste)': 'pasted($event)',
  },
})
export class VisualSearchPanel {
  protected readonly store = inject(VisualSearchStore);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  /** A picture is being held over the drop zone. */
  protected readonly over = signal(false);

  protected chosen(input: HTMLInputElement, how: PictureGiven): void {
    const picture = input.files?.[0];
    // Emptied, so choosing the same file again is still a change.
    input.value = '';
    if (picture) this.store.give({ picture, how });
  }

  protected dropped(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    const picture = event.dataTransfer?.files[0];
    if (picture) this.store.give({ picture, how: 'dropped' });
  }

  protected held(event: DragEvent, over: boolean): void {
    event.preventDefault();
    this.over.set(over);
  }

  protected pasted(event: ClipboardEvent): void {
    const picture = [...(event.clipboardData?.files ?? [])].find((file) => file.type.startsWith('image/'));
    if (!picture) return;
    event.preventDefault();
    this.store.give({ picture, how: 'pasted' });
  }

  /** A press anywhere that is not the panel — nor the camera, which toggles it itself — closes it. */
  protected outside(event: Event): void {
    const target = event.target;
    if (!(target instanceof Node) || this.host.nativeElement.contains(target)) return;
    if (target instanceof Element && target.closest('[data-visual-search-door]')) return;
    this.store.close();
  }
}
