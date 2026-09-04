import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { AuthConfig, OAuthService } from 'angular-oauth2-oidc';
import { API_BASE_URL } from '@tendero/shared-util';
import { catchError, firstValueFrom, throwError } from 'rxjs';

/** What the API says about the issuer it trusts. */
export interface AuthConfiguration {
  issuer: string;
  authority: string;
  clientId: string;
}

/**
 * The role claim's key. It is the long Microsoft schema URI because that is what
 * `ClaimTypes.Role` serialises to, and the API's RoleClaimType expects exactly
 * it — shortening it on either side breaks the other.
 */
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

/** Who the token says is acting. Display only; the server checks the signature. */
export interface SignedInIdentity {
  subject: string;
  name: string;
  role: string;
}

/**
 * The session, and how one is started.
 *
 * **Authorization Code with PKCE, and the shop never sees a credential.** The
 * previous version posted a `password` grant from the browser, which meant the
 * application handled a username and a password — the one thing an identity
 * provider exists to remove. It also meant there was no login page: our own
 * picker stood in for one, so pointing the API at Keycloak changed the signer
 * and changed nothing a person could see.
 *
 * Now both issuers redirect. Keycloak shows its login form; the development
 * issuer shows three buttons, because it has no passwords and inventing some
 * would be the scope creep that is supposed to end its life. The difference is
 * a page the shop does not render, which is why there is no branch here.
 *
 * It lives in shared because it is BEHAVIOUR — start a session, attach a token,
 * end it — and that gets written once (ADR 0010). What each app does with an
 * unauthenticated visitor is identity, and stays per app: the backoffice guards
 * every route, the storefront guards none.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly oauth = inject(OAuthService);

  /**
   * A signal over a library that has none.
   *
   * `OAuthService` exposes events and getters, so this mirrors it: the events
   * are the only reliable moment to read the token, and everything in the
   * interface reads the signal.
   */
  private readonly token = signal<string | null>(null);

  readonly accessToken = this.token.asReadonly();
  readonly isSignedIn = computed(() => this.token() !== null);

  private configuration: AuthConfiguration | null = null;

  /** Which issuer is in charge, for a screen that wants to name it. */
  issuer(): string | null {
    return this.configuration?.issuer ?? null;
  }

  /**
   * Who the current token says is acting, for the interface to name.
   *
   * It is read from the token's own payload rather than stored beside it. The
   * token IS the statement of who you are, and keeping a second copy is how the
   * bar ends up naming somebody who signed out three minutes ago. The signature
   * is not checked here and does not need to be: this decides what to PRINT,
   * and the server validates every request that does anything.
   */
  readonly identity = computed<SignedInIdentity | null>(() => {
    const claims = readClaims(this.token());
    if (!claims) return null;

    return {
      subject: String(claims['sub'] ?? ''),
      name: String(claims['name'] ?? claims['sub'] ?? ''),
      role: String(claims[ROLE_CLAIM] ?? ''),
    };
  });

  /**
   * Asks the API who signs, configures the client from the answer, and finishes
   * a redirect if this load is the way back from one.
   *
   * **Asking rather than deciding is the whole point.** Which issuer is in
   * charge is a setting on the server, and a client that guessed would be wrong
   * on exactly the day somebody flipped it — which happened: the screens kept
   * talking to the development issuer while the API had moved to Keycloak, so
   * every call answered 401 and the interceptor signed the user out on every
   * navigation.
   *
   * It never throws. An API that is not up yet is a shop that cannot sign you in
   * and can still show you a product, and taking the whole application down over
   * that would be out of all proportion.
   */
  async bootstrap(): Promise<void> {
    try {
      this.configuration = await firstValueFrom(
        this.http.get<AuthConfiguration>(`${this.baseUrl}/api/auth/config`),
      );
    } catch {
      return;
    }

    this.oauth.configure(this.authConfig(this.configuration));

    // Reads the code out of the URL when this load is the return leg, and does
    // nothing when it is not. It also removes the query string, which is why the
    // address bar is clean afterwards.
    await this.oauth.loadDiscoveryDocumentAndTryLogin();

    this.readToken();
    this.returnToWhereTheyWere();

    // **Only when there is something to refresh WITH.** Asking for automatic
    // refresh unconditionally starts a timer that renews a token using a refresh
    // token, and the development issuer does not mint one — so it retried, on a
    // schedule, forever. Keycloak does, so it gets the renewal and the fake does
    // not, which is the honest shape of that difference.
    if (this.oauth.getRefreshToken()) this.oauth.setupAutomaticSilentRefresh();

    // The events are what keep the signal honest across a silent refresh and a
    // token that ages out while the tab is open.
    this.oauth.events.subscribe(() => this.readToken());
  }

  private authConfig(configuration: AuthConfiguration): AuthConfig {
    return {
      issuer: configuration.authority,
      clientId: configuration.clientId,

      // The application's own origin, which is the only redirect target either
      // issuer accepts — Keycloak by its realm's list, the development issuer by
      // refusing anything that is not loopback.
      redirectUri: window.location.origin,
      postLogoutRedirectUri: window.location.origin,

      responseType: 'code',
      scope: 'openid profile',

      // PKCE is not optional here. The development issuer refuses a request with
      // no challenge, which is deliberate: a fake that accepts a weaker flow than
      // the real one teaches the client a habit that breaks on the swap.
      disablePKCE: false,

      // Plain HTTP, because both issuers run on localhost in development. It is
      // tied to the environment rather than to a constant, the same call
      // `Authentication:AllowHttpMetadata` makes on the server's side of the
      // same connection.
      requireHttps: false,

      // The development issuer advertises no userinfo endpoint — there is no
      // user store behind it to describe — and strict validation requires one.
      // What that strictness actually protects, that every endpoint lives under
      // the issuer, holds for both documents and is worth saying rather than
      // relying on.
      strictDiscoveryDocumentValidation: false,

      showDebugInformation: false,
    };
  }

  /**
   * Sends the browser to whichever issuer the API named, carrying where to come
   * back to.
   *
   * The redirect URI is the application's ORIGIN and cannot be anything else:
   * every path would have to be listed in the realm, and a shop with per-culture
   * routes would be listing them forever. So the path travels in `state`, which
   * is the parameter OAuth already round-trips untouched — the same value a
   * client compares to know the answer belongs to its own request.
   *
   * Without it, signing in from the account page landed on the home page, and
   * the person had to find their way back to what they had asked for.
   */
  signIn(returnTo?: string): void {
    if (!this.configuration) return;

    // The current page by default, which is what the storefront wants: somebody
    // pressing sign in on their account page means to come back to it. A screen
    // that exists ONLY to sign in has to say otherwise, or it sends you back to
    // itself — which is how this was wrong the first time.
    this.oauth.initCodeFlow(returnTo ?? window.location.pathname + window.location.search);
  }

  /**
   * Ends the session at the issuer as well as here.
   *
   * `logOut` on its own only clears local storage, which leaves the session open
   * at Keycloak — press sign in again and it hands the token straight back with
   * no page in between, which reads exactly like a broken sign-out.
   */
  signOut(): void {
    this.token.set(null);

    if (this.configuration) this.oauth.logOut();
  }

  /** Clears the local session without a redirect. What a 401 needs. */
  discard(): void {
    this.token.set(null);
    this.oauth.logOut(true);
  }

  /**
   * Puts the browser back on the page that started the sign-in.
   *
   * It rewrites the URL rather than navigating, because this runs inside the
   * application initializer — BEFORE the router's first navigation — so the
   * router reads the restored path and routes there directly. Navigating would
   * mean rendering the home page first and replacing it, which is the flicker
   * this avoids.
   *
   * Only same-origin paths are honoured. `state` comes back from the issuer, and
   * anything that arrives from outside is treated as data: a value that is not a
   * path beginning with a single slash is ignored rather than followed.
   */
  private returnToWhereTheyWere(): void {
    const target = this.oauth.state;
    if (!target) return;

    const path = decodeURIComponent(target);
    if (!path.startsWith('/') || path.startsWith('//')) return;

    window.history.replaceState({}, '', path);
  }

  private readToken(): void {
    this.token.set(this.oauth.getAccessToken() ?? null);
  }
}

/**
 * Attaches the token to calls to OUR API and to nothing else. Without that
 * filter, one day somebody adds a call to a third party and sends them our token
 * along with it.
 */
export const tenderoAuthInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthStore);
  const token = auth.accessToken();

  // Our API, and nothing else. The issuer's own endpoints are deliberately NOT
  // matched: a request that asks for a token has no token to send, and attaching
  // one would post our credential to a third party the day somebody adds a call
  // to one.
  const isOurs = request.url.startsWith('/api');

  const outbound =
    token && isOurs
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;

  return next(outbound).pipe(
    catchError((error: unknown) => {
      // A 401 with a token attached means the token is no longer good — expired,
      // or signed by a key that no longer exists, which is what happens on every
      // restart because the development issuer mints its key per process.
      //
      // It DISCARDS rather than signing out: a redirect to the issuer from inside
      // a failed background request would throw the person off whatever page they
      // were reading. Deciding the session is over is this one's job; deciding
      // where to send them is the screen's.
      if (token && isOurs && error instanceof HttpErrorResponse && error.status === 401) {
        auth.discard();
      }

      return throwError(() => error);
    }),
  );
};

/**
 * The JWT payload, or null for anything that is not one.
 *
 * base64url, so the two URL-safe characters go back and the padding is put back;
 * and decodeURIComponent rather than a bare atob, because a name with an accent
 * in it comes out as mojibake otherwise — which is exactly the kind of thing an
 * es/en shop notices immediately.
 */
function readClaims(token: string | null): Record<string, unknown> | null {
  if (!token) return null;

  const payload = token.split('.')[1];
  if (!payload) return null;

  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((character) => `%${character.charCodeAt(0).toString(16).padStart(2, '0')}`)
        .join(''),
    );

    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    // A token this cannot read is a token nobody should be signed in with, but
    // the request will fail on its own merits: this only decides what to print.
    return null;
  }
}
