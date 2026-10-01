import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { ProductSearchService } from '../../data-access/product-search.service';
import { EmptyTrail, type TrailSnapshot } from '../trail-rules';
import { TrailStore } from '../trail.store';
import { TrailHistoryStore } from './trail-history.store';

/** *Tu historial* (R-8, H-3b; ADR 0047): the whole trail, with the products it names read as cards. */
describe('the trail history store', () => {
  const kept = signal<TrailSnapshot>(EmptyTrail);
  let asked: string[][];

  beforeEach(() => {
    kept.set(EmptyTrail);
    asked = [];
    TestBed.configureTestingModule({
      providers: [
        TrailHistoryStore,
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: TrailStore, useValue: { kept } },
        {
          provide: ProductSearchService,
          useValue: {
            cards: (_culture: string, which: { codes: string[] }) => (
              asked.push(which.codes),
              of({ items: which.codes.map((code) => ({ code }) as SearchHit) })
            ),
          },
        },
      ],
    });
  });

  it('draws nothing and asks nothing for an empty trail', () => {
    const store = TestBed.inject(TrailHistoryStore);
    TestBed.tick();

    expect([store.any(), store.viewed(), asked]).toEqual([false, [], []]);
  });

  it('reads the products the trail names as cards, in its order', () => {
    kept.set({
      ...EmptyTrail,
      views: [
        { key: 'B', at: '2026-09-29T10:00:00Z' },
        { key: 'A', at: '2026-09-28T10:00:00Z' },
      ],
    });
    const store = TestBed.inject(TrailHistoryStore);
    TestBed.tick();

    expect([store.any(), store.viewed().map((hit) => hit.code), asked]).toEqual([true, ['B', 'A'], [['B', 'A']]]);
  });
});
