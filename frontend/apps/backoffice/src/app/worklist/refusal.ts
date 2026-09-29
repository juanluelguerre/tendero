import { HttpErrorResponse } from '@angular/common/http';

/**
 * The server's own words for a refusal (ADR 0010), whichever of its two shapes
 * it came in: a slice's rule answers a plain string (409, and 400 for what the
 * request can fix), and the dispatcher's form check answers a validation
 * problem whose messages are grouped by field. Empty when the answer carried no
 * words — then the screen says its own "could not be done".
 */
export function refusalOf(failure: unknown): string {
  if (!(failure instanceof HttpErrorResponse)) return '';
  const body: unknown = failure.error;
  if (typeof body === 'string') return body;
  if (body && typeof body === 'object') {
    const problem = body as { errors?: Record<string, readonly string[]>; detail?: string; title?: string };
    const messages = Object.values(problem.errors ?? {}).flat();
    if (messages.length > 0) return [...new Set(messages)].join(' ');
    return problem.detail ?? problem.title ?? '';
  }
  return '';
}
