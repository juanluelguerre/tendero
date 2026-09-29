import type { CultureEvaluation, StrategyScore } from '@tendero/shared-api';

/**
 * The evaluation as the screen reads it: per language, lexical against hybrid,
 * each difference with its direction, and the queries worst first. Pure — the
 * store derives it, the components say it.
 */
export type Trend = 'better' | 'worse' | 'same';

/** One measure, both strategies, and what hybrid did to it. */
export interface Comparison {
  lexical: number | null;
  hybrid: number | null;
  delta: number | null;
  trend: Trend;
}

export interface QueryRow extends Comparison {
  query: string;
}

export interface CultureView {
  culture: string;
  lexical: StrategyScore;
  hybrid: StrategyScore;
  ndcg: Comparison;
  recall: Comparison;
  rows: QueryRow[];
  better: number;
  worse: number;
}

export function toView(culture: CultureEvaluation): CultureView {
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
export function compare(lexical: number | null, hybrid: number | null): Comparison {
  const delta = lexical === null || hybrid === null ? null : Math.round((hybrid - lexical) * 1000) / 1000;
  const trend: Trend = delta === null || delta === 0 ? 'same' : delta > 0 ? 'better' : 'worse';

  return { lexical, hybrid, delta, trend };
}
