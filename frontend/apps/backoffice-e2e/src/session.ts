import { expect, type Page } from '@playwright/test';

/** The seeded shopkeeper. Three identities, no passwords — see `src/DevIssuer`. */
export const SHOPKEEPER = 'Juan Luis';

/**
 * The same subject in both issuers, and they have to agree: a development issuer
 * seeding one set of identities and a realm seeding another is how every test
 * fixture in the project forks in two.
 */
export const SHOPKEEPER_SUBJECT = 'juanlu';

/**
 * Signs in through the screen, on every spec, on purpose.
 *
 * The obvious optimisation is a cached `storageState`, and it does not work
 * here: the development issuer mints its signing key **per process**, so a token
 * saved by yesterday's run is invalid the moment the API restarts. Signing in
 * takes one click, and a fixture that failed mysteriously after every restart
 * would cost far more than it saved.
 */
export async function signIn(
  page: Page,
  who: { name: string; subject: string } = { name: SHOPKEEPER, subject: SHOPKEEPER_SUBJECT },
): Promise<void> {
  await page.goto('/sign-in');

  // The second step happens on ANOTHER ORIGIN, and that is the whole point of
  // the change this helper had to follow: the shop no longer knows who exists
  // and no longer collects a credential, so the identity is established on the
  // issuer's own page and the browser comes back with a code.
  await page.getByRole('button', { name: /continue/i }).click();

  // **The same suite runs against both issuers**, which is what makes it a
  // guarantee rather than a demonstration. Whichever one the API named is
  // already on screen by now, so the branch is on the URL and not on a flag
  // this spec would have to be told.
  if (/\/realms\//.test(page.url())) {
    // The seeded shopkeeper from `keycloak/realms/tendero-realm.json`. The
    // password is the username, committed on purpose: a development realm with
    // a credential worth protecting is a credential that should not be in a
    // repository. This is the same call `FakePaymentProvider` makes about card
    // numbers.
    // By form field NAME rather than by label: `username` and `password` are
    // what the OIDC login form posts, so they survive a theme and a Keycloak
    // upgrade — where the visible label is translated and the accessible name
    // `password` matches two elements, the field and the reveal button.
    await page.locator('input[name="username"]').fill(who.subject);
    await page.locator('input[name="password"]').fill(who.subject);
    await page.locator('input[type="submit"], button[type="submit"]').first().click();
  } else {
    await page.getByRole('link', { name: who.name }).click();
  }

  // **Waits for the application to say you are IN, not for the URL to stop
  // saying you are out.** `not.toHaveURL(/sign-in/)` was satisfied the instant
  // the browser left that route — which, with a redirect flow, is while it is
  // still on the ISSUER's domain and no token exists yet. The next `goto` then
  // cut the redirect chain, and the test landed back at the door with a page
  // snapshot that said "Sign in" and no explanation.
  //
  // It was intermittent against the development issuer, whose two hops are
  // fast, and reliable against Keycloak, whose form makes the window wide. The
  // account menu only exists once a token does — the BAR's, because the door
  // that turns a shopper away offers a way out of its own. (It was the sign-out
  // button until the redesign moved that into the menu, R-3.)
  await expect(page.getByRole('banner').getByRole('button', { name: /^(account|cuenta):/i })).toBeVisible();
}
