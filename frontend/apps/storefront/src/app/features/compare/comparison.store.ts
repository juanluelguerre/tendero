import { computed, inject } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import type { SpecSheet } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { map } from 'rxjs';
import { CompareStore, MAX_COMPARED } from '../../compare/compare.store';
import { SpecsService } from '../../data-access/specs.service';
import { statusOf } from '../../failure-status';
import { ShopLinks } from '../../shop-links';
import { valueOf } from '@tendero/shared-util';

/** The codes a comparison URL carries, cleaned: trimmed, upper-cased, unique, at most four. */
export function codesFrom(query: string | null): string[] {
  const codes = (query ?? '')
    .split(',')
    .map((code) => code.trim().toUpperCase())
    .filter((code) => code.length > 0);
  return [...new Set(codes)].slice(0, MAX_COMPARED);
}

export type ComparisonState =
  | { status: 'tooFew' }
  | { status: 'loading' }
  | { status: 'failed' }
  | { status: 'done'; sheet: SpecSheet };

/**
 * The comparison page's own state (P10-7; ADR 0047): the products the URL
 * names and Knowledge's sheet about them. The URL is the comparison — the tray
 * only builds it — so a comparison can be reloaded, shared or opened by an
 * agent that never saw the tray.
 */
export const ComparisonStore = signalStore(
  withProps(() => {
    const route = inject(ActivatedRoute);
    const specs = inject(SpecsService);
    const culture = inject(CultureStore);
    const codes = toSignal(route.queryParamMap.pipe(map((params) => codesFrom(params.get('codes')))), { initialValue: [] as string[] });
    return {
      _compare: inject(CompareStore),
      _router: inject(Router),
      _links: inject(ShopLinks),
      codes,
      _sheet: rxResource({
        params: () => {
          const current = codes();
          return current.length > 1 ? { codes: current, culture: culture.active() } : undefined;
        },
        stream: ({ params }) => specs.get(params.codes, params.culture),
      }),
    };
  }),
  withComputed(({ codes, _sheet }) => ({
    state: computed<ComparisonState>(() => {
      if (codes().length < 2) return { status: 'tooFew' };
      switch (_sheet.status()) {
        case 'resolved':
        case 'local': {
          const sheet = valueOf(_sheet) as SpecSheet;
          // Some codes may name products a shopper cannot see: fewer than two left is not a comparison.
          return sheet.products.length < 2 ? { status: 'tooFew' } : { status: 'done', sheet };
        }
        case 'error':
          // A 404 means none of them is visible, which is the same answer as too few.
          return { status: statusOf(_sheet.error()) === 404 ? 'tooFew' : 'failed' };
        default:
          return { status: 'loading' };
      }
    }),
  })),
  withMethods((store) => ({
    /** Takes one product out of the comparison: out of the URL, and out of the tray. */
    drop(code: string): void {
      const picked = store._compare.items().find((item) => item.code === code);
      if (picked) store._compare.toggle(picked);

      const remaining = store.codes().filter((existing) => existing !== code);
      void store._router.navigate(store._links.compare(), { queryParams: { codes: remaining.join(',') } });
    },
  })),
);
