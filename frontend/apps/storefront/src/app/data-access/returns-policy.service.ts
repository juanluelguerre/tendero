import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import type { ReturnPolicy } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { catchError, of } from 'rxjs';

/**
 * How long the shop gives you to send something back.
 *
 * **The number is the server's**, and that is the whole point of this file. The
 * rule lives in `ReturnRequest.Window` and is enforced in `CanOpenAt`; before
 * this, the order page wrote `{ days: 14 }` into its own template, so shortening
 * the window would have left a screen promising the old one with nothing red
 * anywhere. Two readers now, one source — the same decision `/api/auth/config`
 * records for the issuer.
 *
 * It is fetched once per application: the answer cannot change while the tab is
 * open, and two pages asking for it is not two questions.
 */
@Injectable({ providedIn: 'root' })
export class ReturnsPolicy {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly days = signal<number | null>(null);

  /**
   * Days from DELIVERY — not from the purchase, which is the half a returns
   * promise usually gets wrong.
   *
   * `null` means unknown, and a failed call leaves it that way on purpose:
   * defaulting to fourteen in the browser would be the copy this class exists to
   * delete, and a page inventing the promise would go on offering a window the
   * shop had already shortened. Unknown renders nothing.
   */
  readonly windowDays = this.days.asReadonly();

  constructor() {
    this.http
      .get<ReturnPolicy>(`${this.baseUrl}/api/returns/policy`)
      // Swallowed on purpose, and swallowed HERE rather than in a subscriber so
      // there is one path out of this call. A shopper cannot act on the promise
      // having failed to load, and the page they are on still sells: this is the
      // first thing a product page can afford to lose.
      .pipe(catchError(() => of(null)))
      .subscribe(policy => {
        if (policy) this.days.set(policy.windowDays);
      });
  }
}
