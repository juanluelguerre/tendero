import { Injectable, signal } from '@angular/core';

/**
 * When the dashboard's figures were asked for (R-9). Every widget puts this
 * instant in its request, so moving it asks every widget again at once — by
 * hand now, every minute on a wall, and from a live source in R-10.
 */
@Injectable()
export class DashboardRefresh {
  private readonly instant = signal(Date.now());
  readonly at = this.instant.asReadonly();

  refresh(): void {
    this.instant.set(Date.now());
  }
}
