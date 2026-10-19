import { Component, ElementRef, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Icon } from '@tendero/shared-ui';
import { PictureSheetStore } from '../picture-sheet/picture-sheet.store';
import { canShowCamera, Viewfinder } from '../viewfinder/viewfinder';
import { type PictureGiven, VisualSearchStore } from '../visual-search.store';

/**
 * The panel under the search box where a picture is given (phase 13a; the
 * mock-up, `storefront-visual-search`): dropped, chosen, pasted, or — on a
 * phone — taken with the camera, in the page's viewfinder or, where it cannot
 * be shown, the phone's own camera app (CS-6). It says the one thing that is
 * always true of it, that the picture is not kept, and hands the picture to the
 * picture sheet, where it is shown, marked and answered.
 *
 * It closes on Escape and on a click outside it. Nothing here is the page's
 * accent action: the panel is a way to ask, not the thing asked for.
 */
@Component({
  selector: 'storefront-visual-search-panel',
  imports: [TranslocoDirective, Icon, Viewfinder],
  templateUrl: './visual-search-panel.html',
  host: {
    class: 'block',
    // On the page, not on the panel: the panel may not hold the focus when Escape is pressed.
    '(document:keydown.escape)': 'escaped()',
    '(document:pointerdown)': 'outside($event)',
    '(document:paste)': 'pasted($event)',
  },
})
export class VisualSearchPanel {
  protected readonly store = inject(VisualSearchStore);
  private readonly sheet = inject(PictureSheetStore);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  /** A picture is being held over the drop zone. */
  protected readonly over = signal(false);
  /** The viewfinder is showing the camera. */
  protected readonly viewing = signal(false);

  protected chosen(input: HTMLInputElement, how: PictureGiven): void {
    const picture = input.files?.[0];
    // Emptied, so choosing the same file again is still a change.
    input.value = '';
    if (picture) this.give(picture, how);
  }

  /** The camera: in the page where it can be shown, the phone's own app where it cannot. */
  protected camera(app: HTMLInputElement): void {
    if (canShowCamera()) this.viewing.set(true);
    else app.click();
  }

  /** Refused, absent or failed: the phone's own camera app, so the button never simply fails. */
  protected cameraUnavailable(app: HTMLInputElement): void {
    this.viewing.set(false);
    app.click();
  }

  protected taken(picture: Blob): void {
    this.viewing.set(false);
    this.give(picture, 'viewfinder');
  }

  protected dropped(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    const picture = event.dataTransfer?.files[0];
    if (picture) this.give(picture, 'dropped');
  }

  protected held(event: DragEvent, over: boolean): void {
    event.preventDefault();
    this.over.set(over);
  }

  protected pasted(event: ClipboardEvent): void {
    const picture = [...(event.clipboardData?.files ?? [])].find((file) => file.type.startsWith('image/'));
    if (!picture) return;
    event.preventDefault();
    this.give(picture, 'pasted');
  }

  /** Escape closes the viewfinder first, and only then the panel. */
  protected escaped(): void {
    if (!this.viewing()) this.store.close();
  }

  /** A press anywhere that is not the panel — nor the camera, which toggles it itself — closes it. */
  protected outside(event: Event): void {
    const target = event.target;
    if (this.viewing() || !(target instanceof Node) || this.host.nativeElement.contains(target)) return;
    if (target instanceof Element && target.closest('[data-visual-search-door]')) return;
    this.store.close();
  }

  private give(picture: Blob, how: PictureGiven): void {
    this.store.close();
    this.sheet.start(picture, how);
  }
}
