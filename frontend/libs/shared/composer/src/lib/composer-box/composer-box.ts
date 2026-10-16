import { Component, computed, input, output, signal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Composer, Icon } from '@tendero/shared-ui';
import { MicrophoneButton } from '../voice/microphone/microphone';
import type { MicrophonePlace } from '../voice/microphone/microphone.store';

/** A way to add something to a question besides words. */
export type AttachKind = 'photo' | 'camera' | 'file';

/**
 * The box a person asks an agent in, the same for Tino in the shop and Steward in
 * the backoffice (the sketches of 2026-10-17, 03 and 04): one border round the
 * field and its buttons. Each surface says what it accepts and draws none of it:
 *
 * - **voice**: where the microphone writes, or nothing. It is drawn only where an
 *   adapter can listen (invariant 8), and only while the box is empty: with words
 *   in it, typed or heard, the arrow takes its place.
 * - **attach**: the ways to add something. None, nothing at the start; one, its own
 *   icon; two or more, a "+" that opens them, labelled. Neither surface turns any
 *   on yet (the author, 2026-10-17): the rule is here for the day one does.
 *
 * While a turn is out the arrow is a square that stops it. The arrow and the
 * square are the box's one clay action; the microphone and the others stay quiet.
 */
@Component({
  selector: 'lib-composer-box',
  exportAs: 'composerBox',
  imports: [TranslocoDirective, Composer, Icon, MicrophoneButton],
  templateUrl: './composer-box.html',
  host: { class: 'grid gap-2' },
})
export class ComposerBox {
  /** The field's id, for the label and for whoever focuses it. */
  readonly inputId = input.required<string>();
  /** The field's name for a screen reader. */
  readonly label = input.required<string>();
  readonly placeholder = input.required<string>();
  readonly sendLabel = input.required<string>();
  readonly stopLabel = input.required<string>();
  /** A turn is on its way. */
  readonly busy = input(false);
  /** Where the microphone writes, for the evidence of use; null offers none. */
  readonly voice = input<MicrophonePlace | null>(null);
  /** The ways to add something besides words, none by default. */
  readonly attach = input<readonly AttachKind[]>([]);
  /** The targets' size: the shop's 44 px taps or the backoffice's controls. */
  readonly size = input<'tap' | 'control'>('tap');

  /** The question, as it was typed or heard and then sent. */
  readonly asked = output<string>();
  /** Stop the turn on its way. */
  readonly stopped = output<void>();
  /** A way to add something was chosen. */
  readonly attached = output<AttachKind>();

  protected readonly box = viewChild.required(Composer);
  protected readonly mic = viewChild(MicrophoneButton);
  protected readonly menuOpen = signal(false);

  /** One way to add something: its own icon. More: a "+" with a menu. */
  protected readonly single = computed(() => (this.attach().length === 1 ? this.attach()[0] : null));
  protected readonly several = computed(() => this.attach().length > 1);

  /** Whether the microphone is drawn: offered, and the box empty. */
  protected readonly listens = computed(() => this.mic()?.available() === true);

  protected choose(kind: AttachKind): void {
    this.menuOpen.set(false);
    this.attached.emit(kind);
  }

  protected sent(question: string): void {
    this.mic()?.sent(question);
    this.asked.emit(question);
  }
}
