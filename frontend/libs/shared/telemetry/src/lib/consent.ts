import { Injectable, signal } from '@angular/core';

/** What the shopper said about analytics: nothing yet, yes, or no. */
export type ConsentState = 'unasked' | 'granted' | 'denied';

const STORAGE_KEY = 'tendero.analytics-consent';

/**
 * Analytics consent (ADR 0036). Technical telemetry — errors, Web Vitals, the
 * spans a request makes — flows without it, because it carries no personal
 * data and keeps the shop working. A **behavioural** event, one that says what
 * a person did, is dropped until they agree: the tracking plan says which is
 * which, and the facade reads it (`TRACKED_EVENT_CONSENT`).
 *
 * Nothing is asked twice: the answer is remembered in this browser, and
 * forgetting it — private mode, cleared storage — asks again rather than
 * assuming yes.
 */
@Injectable({ providedIn: 'root' })
export class AnalyticsConsent {
  private readonly current = signal<ConsentState>(read());

  readonly state = this.current.asReadonly();

  grant(): void {
    this.answer('granted');
  }

  deny(): void {
    this.answer('denied');
  }

  private answer(state: Exclude<ConsentState, 'unasked'>): void {
    this.current.set(state);
    try {
      localStorage.setItem(STORAGE_KEY, state);
    } catch {
      // Without persistence the answer lasts as long as the tab. Acceptable.
    }
  }
}

function read(): ConsentState {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === 'granted' || value === 'denied' ? value : 'unasked';
  } catch {
    return 'unasked';
  }
}
