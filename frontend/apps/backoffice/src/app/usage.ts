import { HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { StewardAnswer } from '@tendero/shared-api';
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
/** A batch a model proposed: Steward's proposals, or a product's claims together. */
export type ReviewedKind = TrackedEvents['proposals_reviewed']['kind'];
type Reviewed = Omit<TrackedEvents['proposals_reviewed'], 'kind'>;

/** A turn's status as the tracking plan spells it. */
const Statuses: Record<StewardAnswer['status'], TrackedEvents['steward_answered']['status']> = {
  Answered: 'answered',
  Unavailable: 'unavailable',
  BudgetExceeded: 'budget_exceeded',
  ConversationTooLong: 'conversation_too_long',
};

const Views: readonly BackofficeView[] = [
  'dashboard',
  'dashboard.sales',
  'dashboard.operations',
  'dashboard.growth',
  'dashboard.agents',
  'review',
  'catalogue',
  'claims',
  'attributes',
  'search',
  'promotions',
  'campaigns',
  'stock',
  'orders',
  'returns',
  'agents',
  'audit',
  'variants',
  'proposals',
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
  if (segments[0] === 'steward' && segments[1] === 'proposals') return 'proposals';
  const view = (
    segments[0] === 'dashboard' && segments[1] ? `dashboard.${segments[1]}` : (segments[0] ?? 'dashboard')
  ) as BackofficeView;
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
      error: (failure: unknown) =>
        this.telemetry.trackEvent('backoffice_action_taken', { view, action, via, outcome: outcomeOf(failure) }),
    });
  }

  /** Steward's panel opened, and on which screen (phase 12): is it used, and from where? */
  stewardOpened(url: string): void {
    const view = viewOf(url);
    if (view) this.telemetry.trackEvent('steward_opened', { view });
  }

  /**
   * A turn came back: what it read, stored and cost, which is what decides the
   * tools and the default profile — never the question's words or the answer's.
   * An unavailable turn is reported once more on its own, for the profile choice.
   */
  stewardAnswered(answer: StewardAnswer): void {
    this.telemetry.trackEvent('steward_answered', {
      status: Statuses[answer.status],
      tools: answer.reads.length,
      proposals: answer.proposalIds.length,
      calls: answer.cost?.calls ?? 0,
      cost: answer.cost?.microEuros ?? 0,
      fell_back: answer.fellBack,
    });
    if (answer.status === 'Unavailable')
      this.telemetry.trackEvent('steward_unavailable', { fell_back: answer.fellBack });
  }

  /** A batch a model proposed, decided through the review: reviewed, or stamped? */
  proposalsReviewed(kind: ReviewedKind, counts: Reviewed): void {
    this.telemetry.trackEvent('proposals_reviewed', { kind, ...counts });
  }

  /** Something reached through the palette or a list shortcut (R-4.6): are they used? */
  affordance(affordance: Affordance, target: AffordanceTarget): void {
    this.telemetry.trackEvent('backoffice_affordance_used', { affordance, target });
  }
}
