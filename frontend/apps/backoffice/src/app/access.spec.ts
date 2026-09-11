import { describe, expect, it } from 'vitest';
import type { SignedInIdentity } from '@tendero/shared-auth';
import { backofficeAccess } from './access';

/**
 * Who the backoffice lets in.
 *
 * The guard used to ask one question — is anybody signed in? — and the UI
 * review of 2026-09-11 walked straight in as a shopper: every tab, an "Import
 * the sample catalog" button the API refuses, and each 403 rendered as "could
 * not be loaded". The server was right every time; the interface offered what
 * the server was going to deny, which is the exact thing its own comment said
 * the guard existed to prevent.
 */
const who = (role: string): SignedInIdentity => ({ subject: 's', name: 'Somebody', role });

describe('the backoffice door', () => {
  it('lets a shopkeeper in', () => {
    expect(backofficeAccess(who('shopkeeper'))).toBe('allowed');
  });

  it('sends somebody who is not signed in to sign in', () => {
    expect(backofficeAccess(null)).toBe('signed-out');
  });

  it('turns a shopper away rather than letting them in to be refused', () => {
    expect(backofficeAccess(who('shopper'))).toBe('not-a-shopkeeper');
  });

  /** An agent is a principal of its own and has no screen here until phase 11. */
  it('turns an agent away', () => {
    expect(backofficeAccess(who('agent'))).toBe('not-a-shopkeeper');
  });

  /** A token with no role is not a shopkeeper who forgot to say so. */
  it('turns a token with no role away', () => {
    expect(backofficeAccess(who(''))).toBe('not-a-shopkeeper');
  });
});
