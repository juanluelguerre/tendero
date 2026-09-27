import { Injectable, signal } from '@angular/core';

/** Whether this staff member lets the shop measure how its tools are used. */
export type StaffUsageState = 'on' | 'off';

const STORAGE_KEY = 'tendero.staff-usage';
const NOTICE_KEY = 'tendero.staff-usage-notice';

/**
 * Measuring the shop's own tools, under a notice to staff (ADR 0041).
 *
 * **Not consent, on purpose.** A staff member cannot freely refuse their employer,
 * so consent is a weak basis for an employee (EDPB guidelines 05/2020). The basis
 * is the shop's legitimate interest in knowing whether its tools work. That holds
 * only with a notice the person has seen, events that never say who they are, and
 * a way to switch it off, which this class keeps. A *staff* event
 * (`TRACKED_EVENT_CONSENT`) is sent unless the state is `off`.
 *
 * The notice is shown once. Its answer is remembered in this browser, like
 * analytics consent, and forgetting it shows the notice again.
 */
@Injectable({ providedIn: 'root' })
export class StaffUsage {
  private readonly current = signal<StaffUsageState>(read(STORAGE_KEY) === 'off' ? 'off' : 'on');
  private readonly acknowledged = signal(read(NOTICE_KEY) === 'seen');

  readonly state = this.current.asReadonly();

  /** Whether the notice has been seen; until it has, the shell shows it. */
  readonly noticeSeen = this.acknowledged.asReadonly();

  acknowledge(): void {
    this.acknowledged.set(true);
    write(NOTICE_KEY, 'seen');
  }

  switchOff(): void {
    this.set('off');
  }

  switchOn(): void {
    this.set('on');
  }

  private set(state: StaffUsageState): void {
    this.current.set(state);
    this.acknowledge();
    write(STORAGE_KEY, state);
  }
}

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Without persistence the answer lasts as long as the tab. Acceptable.
  }
}
