# shopify-connect

Connects a **merchant's own Shopify store** to Tendero, once (phase 16, S2a,
P16-14; `docs/specs/shopify-leave.md`, decision 2).

The development store is reached with the app's client credentials, which only
work for stores in the app's own Shopify organisation. A merchant's store is not
in it: the merchant installs the app, and the install's authorization-code grant
gives the store's **offline token**. This script runs that install from your
machine. The API has no OAuth callback, and no token goes in the database.

## Before you run it

1. In Shopify's **Dev Dashboard**, open the app (*Tendero import*) and add
   `http://localhost:3457/callback` to its **redirect URLs** (another port if you
   pass `--port`).
2. Give the app **custom distribution** to the merchant's store. A custom app's
   offline token does not expire. Shopify requires expiring tokens only of
   public apps: of new ones since 2026-04-01 and of all of them from 2027-01-01
   ([changelog](https://shopify.dev/changelog/expiring-offline-access-tokens-required-for-all-public-apps-as-of-january-1-2027)).
   The script refuses an expiring token rather than store one that dies in an hour.
3. The app's client id and secret must be in the AppHost's user secrets
   (`Parameters:shopify-client-id`, `Parameters:shopify-client-secret`), as for
   the development store.

## Run

```bash
node tools/shopify-connect/connect.mjs their-store.myshopify.com
```

It prints an address. The store's owner opens it, signed in, and approves the
install. The script then:

- checks the answer: the `state` it sent, Shopify's HMAC signature with the
  app's secret, and that the answer is from the store it asked;
- trades the code for the offline token and checks that the store granted every
  scope it asked for;
- writes `Parameters:shopify-store` and `Parameters:shopify-access-token` to the
  AppHost's user secrets, **without printing the token**.

It asks only for what the import reads: `read_products`, `read_translations`,
`read_locales`, `read_inventory`, `read_locations`. No write scope reaches a
merchant's store.

When the AppHost finds `Parameters:shopify-access-token`, it gives the API the
token instead of the app's credentials. To go back to the development store,
remove the token and set the store again:

```bash
dotnet user-secrets remove Parameters:shopify-access-token --project src/AppHost
dotnet user-secrets set Parameters:shopify-store tendero-dev.myshopify.com --project src/AppHost
```

Then rehearse before you import: `POST /api/catalog/import` and
`POST /api/inventory/import` with `"rehearse": true`.
