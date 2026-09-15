import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { CultureEvaluation, SearchEvaluation, StrategyScore } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import type { Subscription } from 'rxjs';
import { SearchService } from '../../data-access/search.service';

type EvaluationState =
  | { status: 'idle' }
  | { status: 'running' }
  | { status: 'ready'; result: SearchEvaluation }
  | { status: 'failed' };

type Trend = 'better' | 'worse' | 'same';

/** One measure, both strategies, and what hybrid did to it. */
interface Comparison {
  lexical: number | null;
  hybrid: number | null;
  delta: number | null;
  trend: Trend;
}

interface QueryRow extends Comparison {
  query: string;
}

interface CultureView {
  culture: string;
  lexical: StrategyScore;
  hybrid: StrategyScore;
  ndcg: Comparison;
  recall: Comparison;
  rows: QueryRow[];
  better: number;
  worse: number;
}

/**
 * The search evaluation panel (P8-11): the golden sets the CI gate commits, asked
 * of lexical and of hybrid search against the shop as it is right now, in two
 * columns with the difference beside them.
 *
 * **It measures on a button, never on opening.** A hundred and twenty golden
 * queries asked twice take seconds, and a tab that spends them every time somebody
 * passes through it would be a tab people learn to avoid.
 *
 * A query the catalogue has no answer for (`not-sold`, `non-product`) is never
 * scored, so it sits at the bottom with a dash in both columns. What the gate's
 * report shows for those — how much a strategy answered anyway — is not on this
 * screen yet.
 *
 * **The queries hybrid LOST on come first**, the same order the gate's report
 * uses: an average that went up can hide a query that went down, and the one
 * that went down is the one a shopkeeper has to read.
 *
 * With the vector layer down, hybrid's column repeats lexical's. That is the
 * degradation working rather than a result, so the screen says so beside the
 * numbers — two identical columns with no explanation would read as "meaning
 * search adds nothing", which is the one conclusion they cannot support.
 */
@Component({
  selector: 'backoffice-search-evaluation',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search-evaluation-page.html',
  styleUrl: './search-evaluation-page.css',
})
export class SearchEvaluationPage {
  private readonly search = inject(SearchService);
  private readonly culture = inject(CultureStore);

  protected readonly state = signal<EvaluationState>({ status: 'idle' });

  protected readonly result = computed(() => {
    const current = this.state();
    return current.status === 'ready' ? current.result : null;
  });

  protected readonly cultures = computed(() => (this.result()?.cultures ?? []).map(toView));

  private readonly decimals = computed(
    () => new Intl.NumberFormat(this.culture.active(), { minimumFractionDigits: 3, maximumFractionDigits: 3 }),
  );

  private readonly signedDecimals = computed(
    () =>
      new Intl.NumberFormat(this.culture.active(), {
        minimumFractionDigits: 3,
        maximumFractionDigits: 3,
        signDisplay: 'exceptZero',
      }),
  );

  private readonly wholeNumbers = computed(
    () => new Intl.NumberFormat(this.culture.active(), { maximumFractionDigits: 0 }),
  );

  /** The evaluation being asked. Running it again cancels it rather than racing it. */
  private inFlight?: Subscription;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.inFlight?.unsubscribe());
  }

  protected run(): void {
    this.inFlight?.unsubscribe();
    this.state.set({ status: 'running' });

    this.inFlight = this.search.evaluation().subscribe({
      next: (result) => this.state.set({ status: 'ready', result }),
      error: (failure: unknown) => {
        if (isSessionExpired(failure)) return;
        this.state.set({ status: 'failed' });
      },
    });
  }

  /** A query with no relevant judgments is not scored, and says so rather than 0. */
  protected score(value: number | null): string {
    return value === null ? '—' : this.decimals().format(value);
  }

  protected difference(value: number | null): string {
    return value === null ? '—' : this.signedDecimals().format(value);
  }

  protected milliseconds(value: number): string {
    return this.wholeNumbers().format(value);
  }

  protected seconds(result: SearchEvaluation): string {
    return result.elapsedSeconds.toFixed(1);
  }

  protected queryCount(result: SearchEvaluation): number {
    return result.cultures.reduce((sum, culture) => sum + culture.queries.length, 0);
  }
}

function toView(culture: CultureEvaluation): CultureView {
  const rows = culture.queries
    .map((query) => ({ query: query.query, ...compare(query.lexicalNdcgAt10, query.hybridNdcgAt10) }))
    // Worst first, and a query nobody scored last: it has nothing to say either way.
    .sort((a, b) => (a.delta ?? Number.POSITIVE_INFINITY) - (b.delta ?? Number.POSITIVE_INFINITY));

  return {
    culture: culture.culture,
    lexical: culture.lexical,
    hybrid: culture.hybrid,
    ndcg: compare(culture.lexical.ndcgAt10, culture.hybrid.ndcgAt10),
    recall: compare(culture.lexical.recallAt50, culture.hybrid.recallAt50),
    rows,
    better: rows.filter((row) => row.trend === 'better').length,
    worse: rows.filter((row) => row.trend === 'worse').length,
  };
}

/**
 * Rounded to the three decimals the screen shows before the trend is decided, so
 * a difference of 0.0000001 — floating point, not relevance — is "same" and not a
 * green arrow nobody can see the reason for.
 */
function compare(lexical: number | null, hybrid: number | null): Comparison {
  const delta = lexical === null || hybrid === null ? null : Math.round((hybrid - lexical) * 1000) / 1000;
  const trend: Trend = delta === null || delta === 0 ? 'same' : delta > 0 ? 'better' : 'worse';

  return { lexical, hybrid, delta, trend };
}
