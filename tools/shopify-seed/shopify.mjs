// The development store's Admin API, for the seeding script (phase 16, P16-6).
// Credentials come from the AppHost's user secrets, the same three the API is
// given, and are never printed: the token is asked for by the client-credentials
// grant and kept for the run.
import { execFileSync } from 'node:child_process';

export const API_VERSION = '2026-10';

function secrets() {
  const json = execFileSync('dotnet', ['user-secrets', 'list', '--json', '--project', 'src/AppHost'], {
    encoding: 'utf8',
  }).replace(/\/\/BEGIN|\/\/END/g, '');
  const all = JSON.parse(json);
  const store = all['Parameters:shopify-store'];
  const clientId = all['Parameters:shopify-client-id'];
  const clientSecret = all['Parameters:shopify-client-secret'];
  if (!store || !clientId || !clientSecret)
    throw new Error('The three Parameters:shopify-* secrets are not all in the AppHost user secrets.');
  return { store, clientId, clientSecret };
}

export async function connect() {
  const { store, clientId, clientSecret } = secrets();
  const granted = await fetch(`https://${store}/admin/oauth/access_token`, {
    method: 'POST',
    body: new URLSearchParams({ grant_type: 'client_credentials', client_id: clientId, client_secret: clientSecret }),
  });
  if (!granted.ok) throw new Error(`The store refused the app's credentials (${granted.status}).`);
  const { access_token: token } = await granted.json();

  /** Sends a query or mutation; waits out THROTTLED; throws on errors. */
  async function graphql(query, variables = {}) {
    for (let attempt = 1; ; attempt++) {
      const response = await fetch(`https://${store}/admin/api/${API_VERSION}/graphql.json`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Shopify-Access-Token': token },
        body: JSON.stringify({ query, variables }),
      });
      const answer = await response.json();
      const throttled = answer.errors?.some((error) => error.extensions?.code === 'THROTTLED');
      if (throttled && attempt < 6) {
        await new Promise((resolve) => setTimeout(resolve, 1000 * attempt));
        continue;
      }
      if (answer.errors) throw new Error(`The store rejected the query: ${JSON.stringify(answer.errors)}`);
      return answer.data;
    }
  }

  return { store, graphql };
}
