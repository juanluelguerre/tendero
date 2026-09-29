import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import type { SpecSheet } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { of, throwError, type Observable } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CompareStore } from '../../compare/compare.store';
import { SpecsService } from '../../data-access/specs.service';
import { codesFrom, ComparisonStore } from './comparison.store';

describe('codesFrom', () => {
  it('reads a shared comparison URL the way a person types it', () => {
    expect(codesFrom(' caf-001, CAF-002 ,,caf-001 ')).toEqual(['CAF-001', 'CAF-002']);
  });

  it('keeps the four the server compares and nothing when there is nothing', () => {
    expect(codesFrom('A,B,C,D,E')).toEqual(['A', 'B', 'C', 'D']);
    expect(codesFrom(null)).toEqual([]);
  });
});

const sheet = (...codes: string[]): SpecSheet => ({ products: codes.map((code) => ({ code })), statements: [] }) as unknown as SpecSheet;

/** The comparison page's own state (P10-7; ADR 0047): the URL is the comparison, and the sheet is Knowledge's answer about it. */
describe('the comparison store', () => {
  let codes: string;
  let answer: () => Observable<SpecSheet>;
  let asked: number;
  let went: unknown[];
  let toggled: string[];

  beforeEach(() => {
    codes = 'caf-001,caf-002';
    answer = () => of(sheet('CAF-001', 'CAF-002'));
    asked = 0;
    went = [];
    toggled = [];
  });

  function opened() {
    TestBed.configureTestingModule({
      providers: [
        ComparisonStore,
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({ codes })) } },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: SpecsService, useValue: { get: () => (asked++, answer()) } },
        { provide: Router, useValue: { navigate: async (_: unknown, extras: unknown) => void went.push(extras) } },
        {
          provide: CompareStore,
          useValue: { items: () => [{ code: 'CAF-001', name: 'Moka' }], toggle: (item: { code: string }) => void toggled.push(item.code) },
        },
      ],
    });
    const store = TestBed.inject(ComparisonStore);
    TestBed.tick();
    return store;
  }

  it('compares the products the URL names', () => {
    expect(opened().state().status).toBe('done');
  });

  /** One product is not a comparison: nothing is asked. */
  it('asks nothing for fewer than two, and reads a sheet that kept fewer than two as too few', () => {
    codes = 'caf-001';
    const one = opened().state().status;
    const askedForOne = asked;
    TestBed.resetTestingModule();
    codes = 'caf-001,caf-002';
    answer = () => of(sheet('CAF-001'));

    expect([one, askedForOne, opened().state().status]).toEqual(['tooFew', 0, 'tooFew']);
  });

  /** A 404 means none of them is visible, which is the same answer as too few; anything else failed. */
  it('reads a 404 as too few, and anything else as failed', () => {
    answer = () => throwError(() => ({ status: 404 }));
    const missing = opened().state().status;
    TestBed.resetTestingModule();
    answer = () => throwError(() => ({ status: 503 }));

    expect([missing, opened().state().status]).toEqual(['tooFew', 'failed']);
  });

  it('drops a product from the URL and from the tray', () => {
    opened().drop('CAF-001');

    expect([toggled, went]).toEqual([['CAF-001'], [{ queryParams: { codes: 'CAF-002' } }]]);
  });
});
