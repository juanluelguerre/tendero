import { HttpErrorResponse } from '@angular/common/http';
import { refusalSentence } from '@tendero/shared-util';

/**
 * The server's own words for a refusal (ADR 0010). Every one is a problem with
 * a code since phase 13b — a slice's rule and the dispatcher's form check alike
 * — so there is one shape to read: the shop's sentence in the reader's language,
 * and, on a shopkeeper's screen, the domain's own reason beside it. Empty when
 * the answer carried no words — then the screen says its own "could not be done".
 */
export function refusalOf(failure: unknown): string {
  return failure instanceof HttpErrorResponse ? refusalSentence(failure.error, { withReason: true }) : '';
}
