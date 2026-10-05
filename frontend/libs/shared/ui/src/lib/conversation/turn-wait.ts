import { Component, computed, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { timer } from 'rxjs';

/** After this many seconds the wait says it can last, when its caller has words for that. */
export const SlowAfterSeconds = 15;

/**
 * What a panel shows while a turn with an agent is out: the seconds it has been
 * waiting, and, once it is past what a hosted model takes, that it may need
 * longer — if the caller says so (a local model on a CPU takes a minute or
 * more; Tino's turn is cut at twenty seconds and has nothing to add). The server
 * answers a turn in one piece, so this is the elapsed time and nothing it does
 * not know — never a made-up step. It lives only while the turn is asking, so
 * each wait counts from zero. The words are the caller's, in its language.
 */
@Component({
  selector: 'lib-turn-wait',
  templateUrl: './turn-wait.html',
  host: { class: 'contents' },
})
export class TurnWait {
  /** What the agent is doing, in the reader's language. */
  readonly working = input.required<string>();
  /** Said once the wait is long; with none, nothing is. */
  readonly slow = input<string>();

  protected readonly seconds = toSignal(timer(0, 1000), { initialValue: 0 });
  protected readonly late = computed(() => this.seconds() >= SlowAfterSeconds);
}
