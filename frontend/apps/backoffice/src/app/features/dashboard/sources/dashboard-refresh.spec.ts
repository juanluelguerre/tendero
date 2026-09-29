import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DashboardRefresh } from './dashboard-refresh';

describe('the dashboard refresh', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [DashboardRefresh] });
  });
  afterEach(() => vi.useRealTimers());

  it('asks again every minute while it ticks, and not after it stops', () => {
    const refresh = TestBed.inject(DashboardRefresh);
    const first = refresh.at();
    refresh.every(60_000);
    vi.advanceTimersByTime(60_000);
    const second = refresh.at();
    refresh.stop();
    vi.advanceTimersByTime(180_000);

    expect([second > first, refresh.at()]).toEqual([true, second]);
  });

  it('stops ticking when the dashboard goes', () => {
    const refresh = TestBed.inject(DashboardRefresh);
    refresh.every(60_000);
    const at = refresh.at();
    TestBed.resetTestingModule();
    vi.advanceTimersByTime(120_000);

    expect(refresh.at()).toBe(at);
  });
});
