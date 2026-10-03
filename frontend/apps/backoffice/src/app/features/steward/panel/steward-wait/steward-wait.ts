import { Component, computed } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import { timer } from 'rxjs';

/** After this many seconds the wait says it can last: a local model on a CPU takes a minute or more. */
export const SlowAfterSeconds = 15;

/**
 * What the panel shows while a turn is out (phase 12): the seconds it has been
 * waiting, and, once it is past what a hosted model takes, that a local one may
 * need longer. The server answers a turn in one piece, so this is the elapsed
 * time and nothing it does not know — never a made-up step. It lives only while
 * the turn is asking, so each wait counts from zero.
 */
@Component({
  selector: 'backoffice-steward-wait',
  imports: [TranslocoDirective],
  templateUrl: './steward-wait.html',
  host: { class: 'contents' },
})
export class StewardWait {
  protected readonly seconds = toSignal(timer(0, 1000), { initialValue: 0 });
  protected readonly slow = computed(() => this.seconds() >= SlowAfterSeconds);
}
