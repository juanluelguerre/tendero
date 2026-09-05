import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthStore } from '@tendero/shared-auth';

/**
 * The door, and it no longer decides who may come through it.
 *
 * It used to be an identity picker that posted a `password` grant, which put two
 * things in the wrong place: the shop knew who existed, and the shop handled a
 * credential. Both are the identity provider's job, and doing them here meant
 * pointing the API at Keycloak changed the signer while changing nothing a
 * person could see.
 *
 * So this is a button. Pressing it hands the browser to whichever issuer the API
 * named — Keycloak's login form, or the development issuer's list of three — and
 * the way back is a redirect the application initializer finishes before the
 * first route resolves. That is why there is no navigate here: by the time
 * Angular renders again, the guard already passes.
 *
 * The screen lives in the app and not in shared, per ADR 0010: what an
 * unauthenticated visitor is offered is identity, and the storefront offers
 * something different — it lets them shop.
 */
@Component({
  selector: 'backoffice-sign-in',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sign-in-page.html',
  styleUrl: './sign-in-page.css',
})
export class SignInPage {
  private readonly auth = inject(AuthStore);

  private readonly route = inject(ActivatedRoute);

  /**
   * Whether the person is here because their session ENDED.
   *
   * Read from the URL and not from the store, so it survives a reload: somebody
   * who is unexpectedly at the door presses F5 before they read anything, and a
   * flag in memory would have gone by then. The shell puts it there, and only
   * when a token was actually discarded (P7-3's development issuer mints its
   * signing key per process, so every restart of the API ends every session in
   * every browser).
   */
  protected readonly expired =
    this.route.snapshot.queryParamMap.get('reason') === 'expired';

  /** Named so the screen can say who is about to ask for your password. */
  protected readonly issuer = this.auth.issuer();

  /**
   * Where the guard was taking them, or the review queue when they came to the
   * door directly. Never this page: signing in and landing back on the sign-in
   * screen is the shape of a login loop, and it is what happened before the
   * guard started saying where it had intercepted somebody.
   */
  private readonly returnTo = this.route.snapshot.queryParamMap.get('returnTo') ?? '/review';

  protected signIn(): void {
    this.auth.signIn(this.returnTo);
  }
}
