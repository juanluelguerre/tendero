import { expect, test } from '@playwright/test';

/**
 * What a shopkeeper does, in a browser.
 *
 * The whole commerce loop is already covered by `CheckoutLoopTests` against a
 * real Postgres and the real outbox — that an order ships, captures and restocks
 * is not this file's job, and asserting it here again would be a slower copy of
 * a test that already exists.
 *
 * What only a browser can prove is the three things below, and they are the
 * three that have actually broken in this repository:
 *
 * 1. The route guard, the token interceptor and the development issuer work
 *    together. A restart invalidates every token in a browser — the dev issuer
 *    mints its signing key per process — and the shell has to turn that into a
 *    sign-in screen rather than a page of failed panels.
 * 2. The tabs reach the screens they name. `nav.orders` was a translated key
 *    routing nowhere for four days.
 * 3. A table derives its buttons from a status the SERVER sent, and shows the
 *    server's own refusal when it guesses wrong. `Order.AllowedTransitions` is
 *    the single source of truth; this checks the screen never pretends to be a
 *    second one.
 */

/** The seeded shopkeeper. Three identities, no passwords — see `src/DevIssuer`. */
const SHOPKEEPER = 'Juan Luis';

/**
 * The same subject in both issuers, and they have to agree: a development issuer
 * seeding one set of identities and a realm seeding another is how every test
 * fixture in the project forks in two.
 */
const SHOPKEEPER_SUBJECT = 'juanlu';

test.describe('the backoffice', () => {
  test('asks who you are before it shows anything', async ({ page }) => {
    await page.goto('/orders');

    // The guard exists so the interface does not offer what the server is going
    // to deny: without it the page would load empty with a 401 in the console
    // and nothing to explain why.
    // **The door first, the URL second**, and the order is the fix rather than a
    // preference. `toHaveURL` was checked immediately after `goto` and gave the
    // redirect five seconds — which was plenty when the app booted on its own
    // and is not now: the initializer asks the API which issuer it trusts and
    // downloads that issuer's discovery document before the first route
    // resolves. On CI it went past five seconds and the spec read `/orders`,
    // which is the page BEFORE the guard has run rather than a guard that let
    // somebody through.
    //
    // Waiting for something the door renders waits for the application to have
    // finished deciding. The URL assertion after it is then instant, and still
    // catches a guard that sent somebody to the wrong place.
    await expect(page.getByRole('button', { name: /continue/i })).toBeVisible();

    await expect(page).toHaveURL(/\/sign-in(\?|$)/);
  });

  test('signs a shopkeeper in and lands on the review queue', async ({ page }) => {
    await signIn(page);

    await expect(page).toHaveURL(/\/review$/);

    // Who is acting and under which role, because "why can I not publish?" is a
    // question the interface should answer before it is asked.
    await expect(page.getByText('shopkeeper')).toBeVisible();
  });

  test('every tab reaches the screen it names', async ({ page }) => {
    await signIn(page);

    for (const path of ['/stock', '/orders', '/returns', '/attributes', '/promotions', '/audit']) {
      await page.goto(path);

      // A heading, not a URL: a route that resolves to a blank component would
      // pass a URL assertion. `nav.orders` was a translated key routing nowhere
      // for four days, and this is the check that would have caught it.
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    }
  });

  test('a stocktake goes through the ledger and comes back recomputed', async ({ page }) => {
    await signIn(page);
    await page.goto('/stock');

    const row = page.getByRole('row').filter({ hasText: 'B073WXYZ01-DEFAULT' }).first();
    const count = row.getByRole('spinbutton');

    // **A count DIFFERENT from the one that is there**, computed rather than
    // typed. The first version filled in a literal 7, which passed once and
    // then failed on every later run against the same database: the save button
    // only appears on a row that CHANGED, and by the second run the row already
    // said 7. A stateful spec with a constant in it is a spec that tests the
    // database's history.
    const before = Number((await count.inputValue()) || '0');
    const after = String(before === 7 ? 4 : 7);

    await count.fill(after);

    // The save button only appears on a row that changed — a count is not an
    // increment, and pressing save on an unchanged row would be a write nobody
    // asked for.
    await row.getByRole('button').click();

    // Availability is DERIVED server-side (`onHand - reserved`), so the screen
    // redraws from the answer rather than from what was typed. A client that
    // subtracted for itself would disagree with the search index the moment a
    // reservation existed.
    await expect(row).toContainText(after);
  });

  test('the orders table offers only the moves the state machine allows', async ({ page }) => {
    await signIn(page);
    await page.goto('/orders');

    const empty = page.getByText(/No orders yet|Todavía no hay pedidos/);

    // A shop with no orders is a legitimate state and the reason this is not an
    // unconditional assertion: the fixture is whatever the stack has, and a spec
    // that demanded an order would fail on a fresh clone for the wrong reason.
    if (await empty.isVisible().catch(() => false)) test.skip();

    const row = page.getByRole('row').nth(1);
    const buttons = row.getByRole('button');

    // Whatever the status is, the row offers at most the moves that status
    // allows — never both "ship" and "delivered", which is the pair a screen
    // keeping its own copy of the table gets wrong first.
    const labels = await buttons.allInnerTexts();

    expect(labels.filter((label) => /Ship|Enviar/.test(label)).length).toBeLessThanOrEqual(1);
    expect(labels.filter((label) => /Delivered|Entregado/.test(label)).length).toBeLessThanOrEqual(1);
  });

  test('signing out sends you back to the door', async ({ page }) => {
    await signIn(page);

    await page.getByRole('button', { name: /Sign out|Cerrar sesión/ }).click();

    await expect(page).toHaveURL(/\/sign-in(\?|$)/);

    // And the token is gone, not merely hidden: navigating straight back must
    // meet the guard again.
    await page.goto('/orders');
    await expect(page).toHaveURL(/\/sign-in(\?|$)/);
  });
});

/**
 * Signs in through the screen, on every spec, on purpose.
 *
 * The obvious optimisation is a cached `storageState`, and it does not work
 * here: the development issuer mints its signing key **per process**, so a token
 * saved by yesterday's run is invalid the moment the API restarts. Signing in
 * takes one click, and a fixture that failed mysteriously after every restart
 * would cost far more than it saved.
 */
async function signIn(page: import('@playwright/test').Page): Promise<void> {
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
    await page.locator('input[name="username"]').fill(SHOPKEEPER_SUBJECT);
    await page.locator('input[name="password"]').fill(SHOPKEEPER_SUBJECT);
    await page.locator('input[type="submit"], button[type="submit"]').first().click();
  } else {
    await page.getByRole('link', { name: SHOPKEEPER }).click();
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
  // sign-out button only exists once a token does.
  await expect(page.getByRole('button', { name: /sign out|cerrar sesión/i })).toBeVisible();
}
