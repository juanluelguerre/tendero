import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { describe, expect, it } from 'vitest';
import { countBy, keptCounts, statusChips, type StatusCount } from './status-chips';

describe('the status chips', () => {
  const labels = { Draft: 'Borrador', Active: 'Publicado', all: 'Todos' };

  it('are the same chips whatever the counts, an empty status saying 0', () => {
    const counts = new Map([['Active', 5]]);

    expect(statusChips(['Draft', 'Active'], labels, counts)).toEqual([
      { key: 'Draft', label: 'Borrador', count: 0 },
      { key: 'Active', label: 'Publicado', count: 5 },
      { key: 'all', label: 'Todos', count: 5 },
    ]);
  });

  it('say no number before the first answer, rather than a zero that is not true', () => {
    expect(statusChips(['Draft'], labels, null).map((chip) => chip.count)).toEqual([null, null]);
  });

  it('keep the last counts while the next answer is on its way', () => {
    const answered = signal<readonly StatusCount[] | undefined>([{ status: 'Active', count: 5 }]);
    const counts = TestBed.runInInjectionContext(() => keptCounts(() => answered()));

    expect(counts()?.get('Active')).toBe(5);
    answered.set(undefined);
    expect(counts()?.get('Active')).toBe(5);
    answered.set([{ status: 'Active', count: 2 }]);
    expect(counts()?.get('Active')).toBe(2);
  });

  it('count a list answered whole by the key each item falls under', () => {
    const items = [{ s: 'Draft' }, { s: 'Active' }, { s: 'Draft' }];

    expect(countBy(items, (item) => item.s)).toEqual([
      { status: 'Draft', count: 2 },
      { status: 'Active', count: 1 },
    ]);
    expect(countBy([], () => 'x')).toEqual([]);
  });
});
