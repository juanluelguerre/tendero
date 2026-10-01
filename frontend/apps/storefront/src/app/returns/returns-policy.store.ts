import { HttpClient } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { ReturnPolicy } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { extendResource } from '@ngrx/signals/resource';

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
 * open, and two pages asking for it is not two questions (ADR 0047).
 */
export const ReturnsPolicyStore = signalStore(
  { providedIn: 'root' },
  withProps(() => {
    const http = inject(HttpClient);
    const baseUrl = inject(API_BASE_URL);
    return {
      _policy: extendResource(rxResource({ stream: () => http.get<ReturnPolicy>(`${baseUrl}/api/returns/policy`) })),
    };
  }),
  withComputed(({ _policy }) => ({
    /**
     * Days from DELIVERY — not from the purchase, which is the half a returns
     * promise usually gets wrong.
     *
     * `null` means unknown, and a failed call leaves it that way on purpose:
     * defaulting to fourteen in the browser would be the copy this store exists
     * to delete, and a page inventing the promise would go on offering a window
     * the shop had already shortened. Unknown renders nothing.
     */
    windowDays: computed<number | null>(() => (_policy.hasValue() ? _policy.value().windowDays : null)),
  })),
);
