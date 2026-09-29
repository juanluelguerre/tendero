import { TestBed } from '@angular/core/testing';
import { CompareStore, MAX_COMPARED } from './compare.store';

describe('CompareStore', () => {
  beforeEach(() => localStorage.clear());

  it('adds and removes by code, and remembers the pick for the next page', () => {
    const store = TestBed.inject(CompareStore);

    store.toggle({ code: 'CAF-001', name: 'Moka' });
    store.toggle({ code: 'CAF-002', name: 'Goteo' });
    store.toggle({ code: 'CAF-001', name: 'Moka' });

    expect(store.codes()).toEqual(['CAF-002']);
    expect(JSON.parse(localStorage.getItem('tendero.compare') ?? '[]')).toEqual([{ code: 'CAF-002', name: 'Goteo' }]);
  });

  it('stops at the four the server compares', () => {
    const store = TestBed.inject(CompareStore);

    for (let n = 1; n <= MAX_COMPARED + 1; n++) store.toggle({ code: `CAF-00${n}`, name: `${n}` });

    expect(store.items().length).toBe(MAX_COMPARED);
    expect(store.full()).toBe(true);
    expect(store.has('CAF-005')).toBe(false);
  });

  it('ignores what it cannot read rather than failing the page', () => {
    localStorage.setItem('tendero.compare', '{not json');

    expect(TestBed.inject(CompareStore).items()).toEqual([]);
  });
});
