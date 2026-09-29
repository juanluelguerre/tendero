import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { refusalOf } from './refusal';

describe('the words of a refusal', () => {
  it('are the body when a rule answered a string', () => {
    expect(refusalOf(new HttpErrorResponse({ status: 409, error: "Campaign 'X' is Ended and cannot become Scheduled." }))).toBe(
      "Campaign 'X' is Ended and cannot become Scheduled.",
    );
  });

  it('are every message of a validation problem, once each', () => {
    const problem = {
      title: 'The request is not valid.',
      errors: { EndsAt: ["A campaign's window ends after it starts."], PromotionCodes: ['A campaign groups at least one promotion.'], Other: ["A campaign's window ends after it starts."] },
    };

    expect(refusalOf(new HttpErrorResponse({ status: 400, error: problem }))).toBe(
      "A campaign's window ends after it starts. A campaign groups at least one promotion.",
    );
  });

  it('fall back to the problem title, and to nothing when there are no words', () => {
    expect(refusalOf(new HttpErrorResponse({ status: 400, error: { title: 'The request is not valid.' } }))).toBe('The request is not valid.');
    expect(refusalOf(new HttpErrorResponse({ status: 500, error: null }))).toBe('');
    expect(refusalOf(new Error('boom'))).toBe('');
  });
});
