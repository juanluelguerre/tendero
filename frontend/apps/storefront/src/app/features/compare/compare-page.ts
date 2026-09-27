import { ChangeDetectionStrategy, Component, computed, effect, inject, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { SpecSheet, SpecStatement } from '@tendero/shared-api';
import { Telemetry } from '@tendero/shared-telemetry';
import { map } from 'rxjs';
import { CompareStore, MAX_COMPARED } from '../../data-access/compare.store';
import { SpecsService } from '../../data-access/specs.service';
import { ShopLinks } from '../../shop-links';
import { SpecSource } from '../../specs/spec-source';
import { SpecValue } from '../../specs/spec-value';

/** The codes a comparison URL carries, cleaned: trimmed, upper-cased, unique, at most four. */
export function codesFrom(query: string | null): string[] {
  const codes = (query ?? '')
    .split(',')
    .map((code) => code.trim().toUpperCase())
    .filter((code) => code.length > 0);
  return [...new Set(codes)].slice(0, MAX_COMPARED);
}

type CompareState =
  | { status: 'tooFew' }
  | { status: 'loading' }
  | { status: 'failed' }
  | { status: 'done'; sheet: SpecSheet };

/**
 * Two to four products side by side (P10-7): the same rows the product page
 * shows, aligned, each value with its source — and "no data" where a product
 * has none, said in words rather than left blank (ADR 0018).
 *
 * The URL is the comparison. The tray only builds it, so a comparison can be
 * reloaded, shared or opened by an agent that never saw the tray.
 */
@Component({
  selector: 'storefront-compare-page',
  imports: [TranslocoDirective, RouterLink, SpecValue, SpecSource],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './compare-page.html',
  styleUrl: './compare-page.css',
})
export class ComparePage {
  protected readonly links = inject(ShopLinks);

  private readonly specs = inject(SpecsService);
  private readonly culture = inject(CultureStore);
  private readonly compare = inject(CompareStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly telemetry = inject(Telemetry);

  protected readonly codes = toSignal(
    this.route.queryParamMap.pipe(map((params) => codesFrom(params.get('codes')))),
    { initialValue: [] as string[] },
  );

  private readonly sheet = rxResource({
    params: () => {
      const codes = this.codes();
      return codes.length > 1 ? { codes, culture: this.culture.active() } : undefined;
    },
    stream: ({ params }) => this.specs.get(params.codes, params.culture),
  });

  protected readonly state = computed<CompareState>(() => {
    if (this.codes().length < 2) return { status: 'tooFew' };

    switch (this.sheet.status()) {
      case 'resolved':
      case 'local': {
        const sheet = this.sheet.value() as SpecSheet;
        // Some codes may name products a shopper cannot see: fewer than two left is not a comparison.
        return sheet.products.length < 2 ? { status: 'tooFew' } : { status: 'done', sheet };
      }
      case 'error':
        // A 404 means none of them is visible, which is the same answer as too few.
        return { status: statusOf(this.sheet.error()) === 404 ? 'tooFew' : 'failed' };
      default:
        return { status: 'loading' };
    }
  });

  /** The codes last reported, so a language switch is not a second comparison. */
  private reported = '';

  constructor() {
    effect(() => {
      const current = this.state();
      if (current.status !== 'done') return;

      const key = current.sheet.products.map((product) => product.code).join(',');
      if (key === this.reported) return;
      this.reported = key;
      untracked(() => this.telemetry.trackEvent('products_compared', { products: current.sheet.products.length }));
    });
  }

  /** One product's sentences, which are not aligned: two sentences are not a row. */
  protected statementsOf(sheet: SpecSheet, productId: string): SpecStatement[] {
    return sheet.statements.filter((statement) => statement.productId === productId);
  }

  /** Takes one product out of the comparison: out of the URL, and out of the tray. */
  protected drop(code: string): void {
    const picked = this.compare.items().find((item) => item.code === code);
    if (picked) this.compare.toggle(picked);

    const remaining = this.codes().filter((existing) => existing !== code);
    void this.router.navigate(this.links.compare(), { queryParams: { codes: remaining.join(',') } });
  }
}

function statusOf(error: unknown): number | undefined {
  const failure = error as { status?: number; cause?: { status?: number } } | undefined;
  return failure?.status ?? failure?.cause?.status;
}
