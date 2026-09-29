import { inject, Injectable } from '@angular/core';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { AccountService } from './account.service';

/**
 * The customer behind the token, established once per person per tab.
 *
 * Two things need it and neither may assume the other ran first: the account
 * page, which attaches the basket, and the trail, which merges the browser's
 * guest trail into the account at sign-in (R-8, H-3b). Accounts answers an
 * unlinked caller's trail with nothing and keeps nothing for it, so the merge
 * has to wait for the link — `POST /api/accounts/me`, idempotent on the server,
 * and asked here once rather than by each caller.
 */
@Injectable({ providedIn: 'root' })
export class CustomerLink {
  private readonly auth = inject(AuthStore);
  private readonly accounts = inject(AccountService);
  private readonly culture = inject(CultureStore);

  private linked: { readonly subject: string; readonly done: Promise<void> } | null = null;

  /** Resolves once the signed-in person is a customer; a failure is forgotten, so the next call asks again. */
  ensure(): Promise<void> {
    const identity = this.auth.identity();
    if (!identity) return Promise.reject(new Error('Nobody is signed in.'));

    if (this.linked?.subject !== identity.subject) {
      const done = this.accounts.link(identity.name, this.culture.active()).then(() => undefined);
      this.linked = { subject: identity.subject, done };
      done.catch(() => {
        if (this.linked?.done === done) this.linked = null;
      });
    }

    return this.linked.done;
  }
}
