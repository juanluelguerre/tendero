import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { refusalOf } from './refusal';

describe('the words of a refusal', () => {
  /**
   * A rule's refusal is a problem with a code (phase 13b): the shop's sentence
   * in the reader's language, and beside it — this is a shopkeeper's screen —
   * the domain's own reason, which names the rule that said no.
   */
  it('are the shop sentence and the domain reason when a rule refused', () => {
    const refused = {
      code: 'NotAllowed',
      status: 409,
      title: 'La campaña no admite ese paso desde donde está.',
      reason: "Campaign 'X' is Ended and cannot become Scheduled.",
    };

    expect(refusalOf(new HttpErrorResponse({ status: 409, error: refused }))).toBe(
      "La campaña no admite ese paso desde donde está. Campaign 'X' is Ended and cannot become Scheduled.",
    );
  });

  it('are every message of a validation problem, once each', () => {
    const problem = {
      code: 'Invalid',
      title: 'The request is not valid.',
      errors: {
        EndsAt: ["A campaign's window ends after it starts."],
        PromotionCodes: ['A campaign groups at least one promotion.'],
        Other: ["A campaign's window ends after it starts."],
      },
    };

    expect(refusalOf(new HttpErrorResponse({ status: 400, error: problem }))).toBe(
      "A campaign's window ends after it starts. A campaign groups at least one promotion.",
    );
  });

  it('fall back to the problem title, and to nothing when there are no words', () => {
    expect(
      refusalOf(new HttpErrorResponse({ status: 400, error: { code: 'Invalid', title: 'The request is not valid.' } })),
    ).toBe('The request is not valid.');
    expect(refusalOf(new HttpErrorResponse({ status: 502, error: 'Bad Gateway' }))).toBe('');
    expect(refusalOf(new HttpErrorResponse({ status: 500, error: null }))).toBe('');
    expect(refusalOf(new Error('boom'))).toBe('');
  });
});
