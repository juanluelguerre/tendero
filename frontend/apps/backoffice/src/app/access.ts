import type { SignedInIdentity } from '@tendero/shared-auth';

/**
 * The role the API's shopkeeper policy requires, as the token carries it. It is
 * the same string `Authentication.Roles.Shopkeeper` holds on the server; the
 * policy there is what refuses, and this only stops the screen from offering
 * what that policy is going to refuse.
 */
export const SHOPKEEPER_ROLE = 'shopkeeper';

export type BackofficeAccess = 'allowed' | 'signed-out' | 'not-a-shopkeeper';

/**
 * Who the backoffice lets in: a shopkeeper, and nobody else. Signed out and
 * signed in as somebody else are two different answers, because the door says
 * two different things — "sign in" to the first, "this is not your shop's
 * backoffice, sign out to change" to the second.
 */
export function backofficeAccess(identity: SignedInIdentity | null): BackofficeAccess {
  if (identity === null) return 'signed-out';

  return identity.role === SHOPKEEPER_ROLE ? 'allowed' : 'not-a-shopkeeper';
}
