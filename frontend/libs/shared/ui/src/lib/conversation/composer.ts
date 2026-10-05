import {
  afterNextRender,
  computed,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';

/**
 * On the `<textarea>` a question to an agent is typed in. Enter sends and
 * Shift+Enter is a new line; what was sent leaves the box; the box has the
 * focus when it is drawn, and again once an answer is in unless the person went
 * somewhere else. The look, the label and the send button are the caller's:
 *
 *   <form (submit)="$event.preventDefault(); box.send()">
 *     <textarea libComposer #box="libComposer" [busy]="store.busy()" (asked)="ask($event)"></textarea>
 *     <button type="submit" [disabled]="!box.ready()">…</button>
 *   </form>
 *
 * The box itself holds the words — nothing binds its value — so a question typed
 * and sent inside one render still leaves it empty.
 */
@Directive({
  selector: 'textarea[libComposer]',
  exportAs: 'libComposer',
  host: { '(input)': 'typed()', '(keydown)': 'keyed($event)' },
})
export class Composer {
  private readonly box = inject<ElementRef<HTMLTextAreaElement>>(ElementRef).nativeElement;

  /** A turn is on its way: nothing is sent until it is back. */
  readonly busy = input(false);

  /** The question, as it was typed. */
  readonly asked = output<string>();

  private readonly draft = signal('');

  /** Whether there is something to send and nothing on its way: the send button's state. */
  readonly ready = computed(() => !this.busy() && this.draft().trim().length > 0);

  constructor() {
    afterNextRender(() => this.box.focus());

    effect(() => {
      if (this.busy()) return;
      untracked(() => {
        const active = document.activeElement;
        if (!active || active === document.body) this.box.focus();
      });
    });
  }

  send(): void {
    if (!this.ready()) return;
    const text = this.draft();
    this.box.value = '';
    this.draft.set('');
    this.asked.emit(text);
  }

  protected typed(): void {
    this.draft.set(this.box.value);
  }

  protected keyed(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey) return;
    event.preventDefault();
    this.send();
  }
}
