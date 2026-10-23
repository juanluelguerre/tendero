// A merchant's own store, connected once (phase 16, S2a, P16-14). Run from the
// repository root:
//   node tools/shopify-connect/connect.mjs their-store.myshopify.com [--port 3457]
//
// The app's client credentials only reach stores in the app's own organisation;
// a merchant's store installs the app, once, by the authorization-code grant.
// This listens on localhost, prints the address the merchant opens to approve
// the install, checks what Shopify sends back (state, signature, store), trades
// the code for the store's offline token, and writes the store and the token to
// the AppHost's user secrets — `Parameters:shopify-store` and
// `Parameters:shopify-access-token` — never printing the token. Nothing goes in
// the database and the API has no callback (spec decision 2). The app's client
// id and secret are read from the same user secrets.
import { execFileSync } from 'node:child_process';
import { createServer } from 'node:http';
import { authorizeUrl, exchange, isSignedByShopify, isStoreDomain, nonce, SCOPES } from './oauth.mjs';

const args = process.argv.slice(2);
const store = args.find((arg) => !arg.startsWith('--'));
const port = Number(args[args.indexOf('--port') + 1] || 3457);

if (!isStoreDomain(store)) {
  console.error('Usage: node tools/shopify-connect/connect.mjs their-store.myshopify.com [--port 3457]');
  process.exit(1);
}

/** The AppHost's user secrets; a value set this way goes to dotnet as an argument, never through a shell. */
const userSecrets = (...command) =>
  execFileSync('dotnet', ['user-secrets', ...command, '--project', 'src/AppHost'], { encoding: 'utf8' });

const secrets = JSON.parse(userSecrets('list', '--json').replace(/\/\/BEGIN|\/\/END/g, ''));
const clientId = secrets['Parameters:shopify-client-id'];
const clientSecret = secrets['Parameters:shopify-client-secret'];
if (!clientId || !clientSecret) {
  console.error(
    "The app's Parameters:shopify-client-id and Parameters:shopify-client-secret are not in the AppHost user secrets.",
  );
  process.exit(1);
}

const redirectUri = `http://localhost:${port}/callback`;
const state = nonce();

const server = createServer(async (request, response) => {
  const url = new URL(request.url, redirectUri);
  if (url.pathname !== '/callback') {
    response.writeHead(404).end();
    return;
  }

  const refuse = (why) => {
    response.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' }).end(why);
    console.error(why);
  };

  // Each check before the code is spent: a forged callback must not get a token.
  if (url.searchParams.get('state') !== state) return refuse('The answer does not carry the state this install sent.');
  if (!isSignedByShopify(url.searchParams, clientSecret))
    return refuse('The answer is not signed with the app’s secret.');
  if (url.searchParams.get('shop') !== store)
    return refuse(`The answer is from ${url.searchParams.get('shop')}, not ${store}.`);

  try {
    const token = await exchange({
      store,
      clientId,
      clientSecret,
      code: url.searchParams.get('code'),
    });
    userSecrets('set', 'Parameters:shopify-store', store);
    userSecrets('set', 'Parameters:shopify-access-token', token);
    response
      .writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' })
      .end('Connected. You can close this tab and go back to the terminal.');
    console.log(`Connected to ${store}: the store and its token are in the AppHost's user secrets.`);
    console.log('Start the AppHost and import with { "source": "shopify" }; rehearse first with "rehearse": true.');
  } catch (error) {
    refuse(error.message);
  } finally {
    server.close();
  }
});

server.listen(port, '127.0.0.1', () => {
  console.log(`Asking ${store} for: ${SCOPES.join(', ')}.`);
  console.log(`The app must list ${redirectUri} among its redirect URLs (Dev Dashboard, the app's configuration).`);
  console.log('Open this address, signed in as the store’s owner, and approve the install:');
  console.log(`  ${authorizeUrl({ store, clientId, redirectUri, state })}`);
});
