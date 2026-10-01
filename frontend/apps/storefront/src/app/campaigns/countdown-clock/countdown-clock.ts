import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { CultureStore } from '@tendero/shared-i18n';
import { nextChangeIn, spokenTimeLeft, timeLeft } from '../campaign-rules';

/**
 * A campaign's clock: *Termina en 4 d 06 h 12 min* (R-8, H-2).
 *
 * **It wakes once a minute, on the minute, and only itself.** One timer set to the
 * next change of the display (`nextChangeIn`), writing one signal this OnPush
 * component alone reads — never a one-second interval, and never the page.
 *
 * **A screen reader hears it once.** The boxes are drawn for the eye and hidden
 * from assistive technology; what is read is one sentence, *Termina en 4 días, 6
 * horas y 12 minutos*, in an ordinary element and not a live region, so nothing
 * interrupts somebody reading the page every time a minute passes (the spec's
 * accessibility measure).
 */
@Component({
  selector: 'storefront-countdown-clock',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './countdown-clock.html',
  host: { class: 'block' },
})
export class CountdownClock {
  /** The moment counted to, as the server sent it. */
  readonly until = input.required<string>();

  /** What the clock counts to, in words: *Termina en*, *Empieza en*. */
  readonly label = input.required<string>();

  /** The moment has come: the owner asks the server what leads now. */
  readonly reached = output<void>();

  private readonly culture = inject(CultureStore);
  private readonly now = signal(new Date());
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected readonly left = computed(() => timeLeft(this.until(), this.now()));
  protected readonly spoken = computed(() => `${this.label()} ${spokenTimeLeft(this.left(), this.culture.active())}`);

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));

    effect(() => {
      const until = this.until();
      untracked(() => this.schedule(until));
    });
  }

  protected pad(value: number): string {
    return String(value).padStart(2, '0');
  }

  private schedule(until: string): void {
    clearTimeout(this.timer);
    const now = new Date();
    this.now.set(now);

    const wait = nextChangeIn(until, now);
    if (wait === 0) {
      this.reached.emit();
      return;
    }
    this.timer = setTimeout(() => this.schedule(until), wait);
  }
}
