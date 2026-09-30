import { HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { type MonoTypeOperatorFunction, tap } from 'rxjs';

export type BackofficeView = TrackedEvents['backoffice_view_opened']['view'];
/** The screen an action is taken on: the dashboard as a whole, its tabs being views only. */
export type ActionView = TrackedEvents['backoffice_action_taken']['view'];
export type BackofficeAction = TrackedEvents['backoffice_action_taken']['action'];
export type ActionOutcome = TrackedEvents['backoffice_action_taken']['outcome'];
/** Where an action was pressed: the list's row, the pane beside it, or the page itself. */
export type ActionVia = TrackedEvents['backoffice_action_taken']['via'];
export type Affordance = TrackedEvents['backoffice_affordance_used']['affordance'];
export type AffordanceTarget = TrackedEvents['backoffice_affordance_used']['target'];

const Views: readonly BackofficeView[] = [
  'dashboard', 'dashboard.sales', 'dashboard.operations', 'dashboard.growth', 'dashboard.agents', 'review', 'catalogue', 'claims', 'attributes', 'search', 'promotions',
  'campaigns', 'stock', 'orders', 'returns', 'agents', 'audit', 'variants',
];

/**
 * The screen a URL is, by its route and never by an id in it:
 * `/products/0199…/variants` is `variants`, and a dashboard tab is its own view,
 * `/dashboard/sales` is `dashboard.sales` (R-9: which tabs are watched). Null for
 * anything that is not one of the shopkeeper's screens (the sign-in door), which
 * is not measured.
 */
export function viewOf(url: string): BackofficeView | null {
  const segments = url.split(/[?#]/)[0].split('/').filter(Boolean);
  if (segments[0] === 'products' && segments[2] === 'variants') return 'variants';
  const view = (segments[0] === 'dashboard' && segments[1] ? `dashboard.${segments[1]}` : (segments[0] ?? 'dashboard')) as BackofficeView;
  return Views.includes(view) ? view : null;
}

/** done when the server did it; refused when it said no (a 4xx: a rule, a transition); failed otherwise. */
export function outcomeOf(failure: unknown): ActionOutcome {
  return failure instanceof HttpErrorResponse && failure.status >= 400 && failure.status < 500 ? 'refused' : 'failed';
}

/**
 * The backoffice's half of R-0's measurement: which screens are opened and what is
 * done on them, as staff events under the shop's notice (ADR 0041). Nothing here
 * says who acted, only what and where.
 */
@Injectable({ providedIn: 'root' })
export class BackofficeUsage {
  private readonly telemetry = inject(Telemetry);
  private lastPath: string | null = null;

  /**
   * A screen opened, once per route: the same path with another query — a
   * dashboard's period, a list's filter — is the screen already open, not a
   * second opening, and would count it twice.
   */
  opened(url: string): void {
    const path = url.split(/[?#]/)[0];
    if (path === this.lastPath) return;
    this.lastPath = path;
    const view = viewOf(url);
    if (view) this.telemetry.trackEvent('backoffice_view_opened', { view });
  }

  /** The theme a staff member chose in the account menu (R-3): does anybody change it? */
  themeChosen(theme: TrackedEvents['backoffice_theme_chosen']['theme']): void {
    this.telemetry.trackEvent('backoffice_theme_chosen', { theme });
  }

  /**
   * Reports an action once, with how the server answered and where it was
   * pressed: pipe it onto the call that does it,
   * `this.catalog.publish(id).pipe(this.usage.reported('review', 'publish', 'row'))`.
   */
  reported<T>(view: ActionView, action: BackofficeAction, via: ActionVia): MonoTypeOperatorFunction<T> {
    return tap<T>({
      next: () => this.telemetry.trackEvent('backoffice_action_taken', { view, action, via, outcome: 'done' }),
      error: (failure: unknown) => this.telemetry.trackEvent('backoffice_action_taken', { view, action, via, outcome: outcomeOf(failure) }),
    });
  }

  /** Something reached through the palette or a list shortcut (R-4.6): are they used? */
  affordance(affordance: Affordance, target: AffordanceTarget): void {
    this.telemetry.trackEvent('backoffice_affordance_used', { affordance, target });
  }
}
