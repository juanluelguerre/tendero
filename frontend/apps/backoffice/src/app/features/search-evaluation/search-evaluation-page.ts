import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchEvaluation } from '@tendero/shared-api';
import { CultureEvaluation } from './culture-evaluation/culture-evaluation';
import { SearchEvaluationStore } from './search-evaluation.store';
import { Notice } from '@tendero/shared-ui';

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
  imports: [TranslocoDirective, CultureEvaluation, Notice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search-evaluation-page.html',
  styleUrl: './search-evaluation-page.css',
  providers: [SearchEvaluationStore],
})
export class SearchEvaluationPage {
  protected readonly store = inject(SearchEvaluationStore);

  protected seconds(result: SearchEvaluation): string {
    return result.elapsedSeconds.toFixed(1);
  }

  protected queryCount(result: SearchEvaluation): number {
    return result.cultures.reduce((sum, culture) => sum + culture.queries.length, 0);
  }
}
