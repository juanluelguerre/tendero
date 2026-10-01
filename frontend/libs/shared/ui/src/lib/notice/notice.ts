import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type NoticeTone = 'info' | 'good' | 'warn' | 'bad';

/**
 * Something the reader has to read before carrying on — what happened and what
 * to do, never just "error" (design/DESIGN.md) — in one shape for both apps. The
 * words are the caller's; the tone colours the bar beside them, and never
 * carries the meaning alone.
 *
 * `live` is how loudly it arrives: `assertive` interrupts (a failure the reader
 * caused or must act on), `polite` waits (a state worth announcing), and null
 * is read in place with the page.
 */
@Component({
  selector: 'lib-notice',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notice.html',
  // Its size and margins are its place's: the text inherits the page's size, and whoever
  // places it gives it room.
  host: { class: 'block' },
})
export class Notice {
  readonly tone = input<NoticeTone>('info');
  readonly live = input<'polite' | 'assertive' | null>(null);

  protected readonly role = computed(() => (this.live() === 'assertive' ? 'alert' : this.live() === 'polite' ? 'status' : null));
}
