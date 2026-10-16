import { Component, computed, inject, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Icon } from '@tendero/shared-ui';
import { MicrophoneStore } from './microphone.store';

/**
 * A question spoken instead of typed (phase 14; the sketch of 2026-10-16): a
 * neutral button, never clay, drawn only where an adapter can listen in the
 * page's language — otherwise nothing, and the box is as it always was
 * (invariant 8). Pressed to listen and again to finish; Escape cancels.
 *
 * What was heard goes out as `heard`, for the box it belongs to, which writes it
 * there **unsent**. Where the line saying what happened goes is the box's: it
 * reads `status` through `#mic="microphone"`.
 */
@Component({
  selector: 'storefront-microphone',
  exportAs: 'microphone',
  imports: [TranslocoDirective, Icon],
  templateUrl: './microphone.html',
  providers: [MicrophoneStore],
  host: { class: 'contents', '(document:keydown.escape)': 'store.cancel()' },
})
export class Microphone {
  protected readonly store = inject(MicrophoneStore);

  /** Whether its place offers one: the header's box on a desktop does not, a keyboard is there already. */
  readonly offered = input(true);

  /** The words heard, to be written into the box and not sent. */
  readonly heard = output<string>();

  /** Whether it is drawn: offered here, and an adapter can listen in the page's language. For the box's own room. */
  readonly available = computed(() => this.offered() && this.store.available());
  /** What to say beside the box. */
  readonly status = this.store.status;
  /** Whether it is listening: for the box's own "Escuchando…". */
  readonly listening = this.store.listening;

  /** The box's question was sent: the line beside it goes. */
  clear(): void {
    this.store.clear();
  }

  protected async press(): Promise<void> {
    const words = await this.store.press();
    if (words) this.heard.emit(words);
  }
}
