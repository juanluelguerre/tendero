import { DestroyRef, inject, Injectable, signal } from '@angular/core';

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
