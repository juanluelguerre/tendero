import { Component, computed, ElementRef, inject, input, output, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { type PictureBox, share } from '../picture-box';
import { PictureSheetStore, type SheetObject } from '../picture-sheet.store';

/** What a drag is doing to the marked part: drawing it anew, moving it, or pulling its corner. */
type Drag = {
  readonly kind: 'draw' | 'move' | 'resize';
  readonly x: number;
  readonly y: number;
  readonly from: PictureBox;
};

/**
 * The shopper's picture with what the shop sees on it (the camera search; the
 * mock-up, screens 8 and 9): before it is sent, the part being marked, drawn,
 * moved and resized with a pointer or the keyboard (CS-1.2, CS-8); once
 * answered, **a numbered mark on each object** — a button named by its number
 * and kind, which takes the shopper to its block (CS-3).
 *
 * The picture is drawn from the browser's memory, never from the shop. Boxes
 * are kept in the picture's own pixels and drawn as shares of it, so they sit
 * where they belong at whatever size the picture is shown.
 */
@Component({
  selector: 'storefront-picture-marks',
  imports: [TranslocoDirective],
  templateUrl: './picture-marks.html',
  styleUrl: './picture-marks.css',
  host: { class: 'block' },
})
export class PictureMarks {
  protected readonly store = inject(PictureSheetStore);

  /** The object whose block is in view or was named, outlined. */
  readonly outlined = input<number | null>(null);
  /** A mark was pressed: the sheet takes the shopper to its block. */
  readonly picked = output<number>();

  private readonly frame = viewChild.required<ElementRef<HTMLElement>>('frame');
  private drag: Drag | null = null;

  protected readonly marking = computed(() => this.store.step() === 'compose' && this.store.choice() === 'part');
  protected readonly markShare = computed(() => {
    const mark = this.store.mark();
    const size = this.store.size();
    return mark && size ? share(mark, size) : null;
  });
  protected readonly marks = computed(() => {
    const size = this.store.size();
    if (!size || this.store.step() !== 'answered') return [];
    // An object of no kind is what was sent, whole: there is nothing to point at.
    return this.store
      .objects()
      .filter((object) => object.kind !== null)
      .map((object) => ({ object, at: share(object.box, size) }));
  });

  protected loaded(image: HTMLImageElement): void {
    this.store.loaded({ width: image.naturalWidth, height: image.naturalHeight });
  }

  protected label(object: SheetObject): string {
    return `${object.number} · ${object.name ?? ''}`;
  }

  /** A press on the picture while marking: inside the part it moves it, on its corner it resizes it, elsewhere it draws a new one. */
  protected pressed(event: PointerEvent, kind: Drag['kind']): void {
    const mark = this.store.mark();
    const at = this.at(event);
    if (!this.marking() || !at) return;
    event.preventDefault();
    event.stopPropagation();
    (event.currentTarget as Element).setPointerCapture?.(event.pointerId);
    this.drag = { kind, x: at.x, y: at.y, from: mark ?? { left: at.x, top: at.y, width: 0, height: 0 } };
    if (kind === 'draw') this.store.markWith({ left: at.x, top: at.y, width: 0, height: 0 });
  }

  protected moved(event: PointerEvent): void {
    const at = this.at(event);
    const drag = this.drag;
    if (!drag || !at) return;
    const dx = at.x - drag.x;
    const dy = at.y - drag.y;
    const from = drag.from;
    switch (drag.kind) {
      case 'move':
        this.store.markWith({ ...from, left: from.left + dx, top: from.top + dy });
        break;
      case 'resize':
        this.store.markWith({ ...from, width: from.width + dx, height: from.height + dy });
        break;
      case 'draw':
        this.store.markWith({
          left: Math.min(drag.x, at.x),
          top: Math.min(drag.y, at.y),
          width: Math.abs(dx),
          height: Math.abs(dy),
        });
        break;
    }
  }

  protected released(): void {
    this.drag = null;
  }

  /** Arrows move the part, Shift and the arrows resize it (CS-8). */
  protected keyed(event: KeyboardEvent): void {
    if (!event.key.startsWith('Arrow')) return;
    event.preventDefault();
    this.store.nudge(event.key, event.shiftKey);
  }

  /** Where a pointer is, in the picture's own pixels. */
  private at(event: PointerEvent): { x: number; y: number } | null {
    const size = this.store.size();
    const rect = this.frame().nativeElement.getBoundingClientRect();
    if (!size || rect.width === 0 || rect.height === 0) return null;
    return {
      x: ((event.clientX - rect.left) / rect.width) * size.width,
      y: ((event.clientY - rect.top) / rect.height) * size.height,
    };
  }
}
