import { describe, expect, it } from 'vitest';
import { periodFromQuery, queryOfPeriod } from './dashboard-period';

describe('the period in the URL', () => {
  it('reads day, week and month in any case', () => {
    expect(['day', 'WEEK', 'Month'].map(periodFromQuery)).toEqual(['Day', 'Week', 'Month']);
  });

  /** Review focus 1: a stale or odd link still opens the dashboard, on its default week. */
  it('opens on the week when the URL has no period or one it does not know', () => {
    expect([null, '', 'year', '7'].map(periodFromQuery)).toEqual(['Week', 'Week', 'Week', 'Week']);
  });

  it('writes the period in lowercase, as a monitor keeps it', () => {
    expect((['Day', 'Week', 'Month'] as const).map(queryOfPeriod)).toEqual(['day', 'week', 'month']);
  });
});
