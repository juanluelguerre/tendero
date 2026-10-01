import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CategoryView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { ShopFrontService } from '../data-access/shop-front.service';
import { TaxonomyStore } from './taxonomy.store';

const category = (code: string, parent: string | null, depth: number): CategoryView => ({
  code,
  parent,
  name: code,
  path: [],
  depth,
});

/** The shop's categories, read once per language and shared by the menu, the drawer and the filters (ADR 0047). */
describe('the taxonomy store', () => {
  const culture = signal('es');
  let asked: string[];

  beforeEach(() => {
    culture.set('es');
    asked = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: CultureStore, useValue: { active: culture } },
        {
          provide: ShopFrontService,
          useValue: {
            categories: (asking: string) => {
              asked.push(asking);
              return of({ items: [category('HOME', null, 0), category(`KITCHEN-${asking}`, 'HOME', 1)] });
            },
          },
        },
      ],
    });
  });

  it('reads the categories in the reader’s language and draws them as a menu', () => {
    const store = TestBed.inject(TaxonomyStore);
    TestBed.tick();

    expect(
      store
        .menu()
        .map((department) => [department.category.code, department.groups.map((group) => group.category.code)]),
    ).toEqual([['HOME', ['KITCHEN-es']]]);
  });

  it('reads them again when the language changes, and only then', () => {
    const store = TestBed.inject(TaxonomyStore);
    TestBed.tick();
    store.categories();
    culture.set('en');
    TestBed.tick();

    expect([asked, store.categories().map((category) => category.code)]).toEqual([
      ['es', 'en'],
      ['HOME', 'KITCHEN-en'],
    ]);
  });
});
