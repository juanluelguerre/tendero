import { afterNextRender, Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { TranslocoDirective } from '@jsverse/transloco';
import { Icon, Notice } from '@tendero/shared-ui';
import { VisualSearchStore } from '../visual-search.store';
import { ObjectBlock } from './object-block/object-block';
import { PictureMarks } from './picture-marks/picture-marks';
import { PictureSheetStore } from './picture-sheet.store';

/**
 * The picture sheet (the camera search, CS-1, CS-3, CS-4, CS-8; the mock-up,
 * screens 8 to 12): full screen on a phone, a dialog on a desktop.
 *
 * Before it is sent, the picture and the choice between searching it whole and
 * marking a part. Answered, the picture with a numbered mark on each object
 * beside **a block an object**, the line of what the shop does not sell, and
 * the field whose words narrow every block. A mark and its block point at each
 * other. Every control is neutral: the page's one clay action is not here.
 *
 * A native modal dialog, as the listing's filters are: the platform holds the
 * focus, Esc closes it and the page behind is inert.
 */
@Component({
  selector: 'storefront-picture-sheet',
  imports: [TranslocoDirective, FormField, Icon, Notice, PictureMarks, ObjectBlock],
  templateUrl: './picture-sheet.html',
  host: { '(document:pagehide)': 'store.close()' },
})
export class PictureSheet {
  protected readonly store = inject(PictureSheetStore);
  protected readonly visual = inject(VisualSearchStore);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  /** The object last pointed at, from its mark or its heading: outlined on the picture. */
  protected readonly outlined = signal<number | null>(null);

  /**
   * The blocks drawn: every object searched, except one answered with nothing
   * near and no words — the line under the blocks says it (CS-1.6).
   */
  protected readonly drawn = computed(() =>
    this.store.searched().filter((object) => {
      const block = this.store.blocks()[object.number];
      return !(block?.phase === 'answered' && block.matches.length === 0 && !this.store.words());
    }),
  );

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  protected send(event: Event): void {
    event.preventDefault();
    this.store.search();
  }

  protected narrow(event: Event): void {
    event.preventDefault();
    this.store.narrowWithField();
  }

  /** A mark pressed: its block brought into view, and its object outlined. */
  protected picked(number: number): void {
    this.outlined.set(number);
    this.host.nativeElement
      .querySelector(`[data-picture-block="${number}"]`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  /** A product opened from a block: counted with its block, and the sheet put away as the page moves on. */
  protected followed(number: number, position: number): void {
    this.visual.followed(position, number);
    this.store.close();
  }
}
