import type { ClaimForReview } from '@tendero/shared-api';

/** A claim's sentence in one language: the list says the reader's, the pane says both. */
export function statementIn(claim: ClaimForReview, culture: string): string {
  return culture === 'en' ? claim.statementEnglish : claim.statementSpanish;
}

/** A support or a quote's score as a percentage, rounded: what a reviewer reads, not the scorer's decimals. */
export function percent(support: number): string {
  return `${Math.round(support * 100)} %`;
}
