import { DestroyRef, effect, inject, Injectable, type ResourceRef, signal, untracked } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';

/**
 * When the dashboard's figures were asked for (R-9). Every widget puts this
 * instant in its request, so moving it asks every widget again at once — by
 * hand, every minute on a wall, and from a live source in R-10.
 */
@Injectable()
export class DashboardRefresh {
  private readonly instant = signal(Date.now());
  private tick: ReturnType<typeof setInterval> | undefined;

  readonly at = this.instant.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  refresh(): void {
    this.instant.set(Date.now());
  }

  /** Until R-10's live source, a wall asks again on a clock (spec: "refreshes every minute, and says so"). */
  every(milliseconds: number): void {
    this.stop();
    this.tick = setInterval(() => this.refresh(), milliseconds);
  }

  stop(): void {
    clearInterval(this.tick);
    this.tick = undefined;
  }
}

/**
 * A widget's read that follows the dashboard's clock (R-9). The instant is not
 * one of its params: a new param makes a resource drop its value and show
 * "Loading…", so a wall refreshed every minute blinked every minute (the
 * whole-branch review). A new instant reloads instead, which keeps the figures
 * on screen until the new ones arrive; a new period is a new question, and
 * starts from empty as it should.
 */
export function refreshedResource<P extends object, T>(options: {
  /** Undefined while there is nothing to ask yet: the resource stays idle. */
  params: () => P | undefined;
  stream: (params: P, at: number) => Observable<T>;
}): ResourceRef<T | undefined> {
  const refresh = inject(DashboardRefresh);
  const resource = rxResource<T, P>({
    // Undefined params keep a resource idle; Angular's type does not say so.
    params: options.params as () => P,
    stream: ({ params }) => options.stream(params, untracked(refresh.at)),
  });

  let asked = untracked(refresh.at);
  effect(() => {
    const at = refresh.at();
    if (at === asked) return;
    asked = at;
    untracked(() => resource.reload());
  });

  return resource;
}
