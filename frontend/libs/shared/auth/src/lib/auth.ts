import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { API_BASE_URL } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';

/**
 * Who can sign in. The issuer serves this, the client does not write it: the day
 * the issuer is Keycloak this list comes from a realm and nothing here changes.
 */
export interface TenderoIdentity {
  subject: string;
  name: string;
  role: string;
  isAgent: boolean;
}

interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
}

const STORAGE_KEY = 'tendero.token';

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
 * The token and who owns it.
 *
 * It lives in shared because it is BEHAVIOUR — store, attach, sign out — and
 * that gets written once (ADR 0010). The login SCREEN is identity: the
 * storefront will end up asking a shopper for an email and a password, and the
 * backoffice picks between seeded identities; sharing that view would produce a
 * component with a variant matrix worse than two components.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly token = signal<string | null>(readStoredToken());

  readonly accessToken = this.token.asReadonly();
  readonly isSignedIn = computed(() => this.token() !== null);

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
   * The development issuer's seeded identities. Returns an empty list when it
   * does not answer: with no issuer there is no login, and an empty screen
   * explains itself better than an exception.
   */
  async identities(): Promise<TenderoIdentity[]> {
    try {
      return await firstValueFrom(
        this.http.get<TenderoIdentity[]>(`${this.baseUrl}/dev-issuer/identities`),
      );
    } catch {
      return [];
    }
  }

  /**
   * `password` for people and `client_credentials` for agents: they are the two
   * flows Keycloak will serve later, so using them now saves the client from
   * changing when the issuer does.
   */
  async signIn(identity: TenderoIdentity): Promise<void> {
    const body = new URLSearchParams(
      identity.isAgent
        ? { grant_type: 'client_credentials', client_id: identity.subject }
        : { grant_type: 'password', username: identity.subject },
    );

    const response = await firstValueFrom(
      this.http.post<TokenResponse>(`${this.baseUrl}/dev-issuer/connect/token`, body.toString(), {
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      }),
    );

    this.token.set(response.accessToken);
    write(response.accessToken);
  }

  signOut(): void {
    this.token.set(null);
    write(null);
  }
}

/**
 * Attaches the token to calls to OUR API and to nothing else. Without that
 * filter, one day somebody adds a call to a third party and sends them our token
 * along with it.
 */
export const tenderoAuthInterceptor: HttpInterceptorFn = (request, next) => {
  const token = inject(AuthStore).accessToken();
  const isOurs = request.url.startsWith('/api') || request.url.startsWith('/dev-issuer');

  return token && isOurs
    ? next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }))
    : next(request);
};

/**
 * localStorage can throw (private mode, blocked cookies), and failing to start
 * the application because a session cannot be remembered would be out of all
 * proportion.
 */
function readStoredToken(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function write(token: string | null): void {
  try {
    if (token) localStorage.setItem(STORAGE_KEY, token);
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Without persistence the session lasts as long as the tab. That is acceptable.
  }
}

/**
 * The JWT payload, or null for anything that is not one.
 *
 * base64url, so the two URL-safe characters go back and the padding is put
 * back; and decodeURIComponent/escape rather than a bare atob, because a name
 * with an accent in it comes out as mojibake otherwise — which is exactly the
 * kind of thing an es/en shop notices immediately.
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
