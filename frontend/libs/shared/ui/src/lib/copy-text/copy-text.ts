import { booleanAttribute, ChangeDetectionStrategy, Component, input, signal } from '@angular/core';

/**
 * Something a person copies — an address, a command — shown and copyable in one
 * place, in both apps. The words are the caller's, so this library stays free of
 * any translation: the button's accessible name says WHAT it copies ("Copiar la
 * dirección"), and the status says it happened, once, to a screen reader.
 *
 * The clipboard is a permission a browser may refuse; the text stays on screen,
 * selectable in one click (`user-select: all`), so a refusal costs a keystroke,
 * never the text.
 */
@Component({
  selector: 'lib-copy-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './copy-text.html',
  styleUrl: './copy-text.css',
})
export class CopyText {
  readonly value = input.required<string>();
  /** The button's accessible name: what it copies, in the reader's language. */
  readonly copyLabel = input.required<string>();
  /** Said once when the copy is done. */
  readonly copiedLabel = input.required<string>();
  /** A command or anything long: a block that keeps its lines, and scrolls rather than wraps. */
  readonly block = input(false, { transform: booleanAttribute });

  protected readonly copied = signal(false);

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.value());
      this.copied.set(true);
    } catch {
      this.copied.set(false);
    }
  }
}
