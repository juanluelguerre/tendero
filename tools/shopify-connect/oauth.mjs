// The authorization-code grant's checks, apart from the server that runs them,
// so each can be read — and tried — on its own
// (https://shopify.dev/docs/apps/build/authentication-authorization/access-tokens/authorization-code-grant).
import { createHmac, randomBytes, timingSafeEqual } from 'node:crypto';

/** What the import reads, and nothing it does not: no write scope reaches a merchant's store. */
export const SCOPES = ['read_products', 'read_translations', 'read_locales', 'read_inventory', 'read_locations'];

/** A store's own domain, anchored: `name.myshopify.com` and nothing around it. */
export const isStoreDomain = (shop) => /^[a-z0-9][a-z0-9-]*\.myshopify\.com$/.test(shop ?? '');

export const nonce = () => randomBytes(16).toString('hex');

/**
 * Where the merchant approves the install. No `grant_options[]`: an offline
 * token, the store's and not a person's, which is what an import needs.
 */
export function authorizeUrl({ store, clientId, redirectUri, state }) {
  const query = new URLSearchParams({
    client_id: clientId,
    scope: SCOPES.join(','),
    redirect_uri: redirectUri,
    state,
  });
  return `https://${store}/admin/oauth/authorize?${query}`;
}

/**
 * The callback came from Shopify: every parameter but `hmac`, sorted and joined
 * as a query string, signed with the app's secret, compared in constant time.
 */
export function isSignedByShopify(params, clientSecret) {
  const given = params.get('hmac');
  if (!given) return false;

  const message = [...params.entries()]
    .filter(([key]) => key !== 'hmac')
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
    .map(([key, value]) => `${key}=${value}`)
    .join('&');
  const expected = createHmac('sha256', clientSecret).update(message).digest('hex');

  return given.length === expected.length && timingSafeEqual(Buffer.from(given), Buffer.from(expected));
}

/**
 * The code for the store's offline token. No `expiring=1`: a custom app's
 * offline token does not expire — Shopify requires expiring ones of public apps
 * only — so the token can be a secret like the others and nothing has to keep
 * refreshing it (spec decision 2).
 */
export async function exchange({ store, clientId, clientSecret, code }) {
  const response = await fetch(`https://${store}/admin/oauth/access_token`, {
    method: 'POST',
    body: new URLSearchParams({
      client_id: clientId,
      client_secret: clientSecret,
      code,
    }),
  });
  if (!response.ok) throw new Error(`The store refused the code (${response.status}).`);

  const granted = await response.json();
  const missing = SCOPES.filter((scope) => !(granted.scope ?? '').split(',').includes(scope));
  if (missing.length > 0) throw new Error(`The store granted less than asked: ${missing.join(', ')} missing.`);
  if (granted.expires_in > 0) throw new Error('The store gave an expiring token: the app is not a custom app.');

  return granted.access_token;
}
