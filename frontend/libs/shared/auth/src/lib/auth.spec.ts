import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { OAuthService } from 'angular-oauth2-oidc';
import { afterEach, describe, expect, it } from 'vitest';
import { AuthStore } from './auth';

/**
 * Signing in sends the browser to the issuer (ADR 0017). The issuer's page is
 * the shop's first page in a language it did not choose unless it is told: the
 * page's own language travels as `ui_locales`, which Keycloak honours on a realm
 * with its languages turned on (keycloak/README.md).
 */
describe('signing in', () => {
  let flows: [string, Record<string, string>][];

  function opened(): AuthStore {
    flows = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        { provide: API_BASE_URL, useValue: '' },
        {
          provide: OAuthService,
          useValue: {
            initCodeFlow: (state: string, params: Record<string, string>) => void flows.push([state, params]),
          },
        },
      ],
    });
    const store = TestBed.inject(AuthStore);
    // What init() reads from /api/auth/config; signing in waits for it.
    (store as unknown as { configuration: unknown }).configuration = {
      authority: 'https://issuer',
      clientId: 'tendero-app',
    };
    return store;
  }

  afterEach(() => document.documentElement.removeAttribute('lang'));

  it('asks the issuer for the page’s own language', () => {
    document.documentElement.lang = 'es';

    opened().signIn('/es/account');

    expect(flows).toEqual([['/es/account', { ui_locales: 'es' }]]);
  });

  it('asks for no language when the page names none', () => {
    opened().signIn('/account');

    expect(flows).toEqual([['/account', {}]]);
  });
});
