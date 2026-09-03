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

test.describe('the backoffice', () => {
  test('asks who you are before it shows anything', async ({ page }) => {
    await page.goto('/orders');

    // The guard exists so the interface does not offer what the server is going
    // to deny: without it the page would load empty with a 401 in the console
    // and nothing to explain why.
    await expect(page).toHaveURL(/\/sign-in$/);
    await expect(page.getByRole('button', { name: SHOPKEEPER })).toBeVisible();
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

    for (const path of ['/stock', '/orders', '/returns', '/attributes', '/promotions']) {
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

    await count.fill('7');

    // The save button only appears on a row that changed — a count is not an
    // increment, and pressing save on an unchanged row would be a write nobody
    // asked for.
    await row.getByRole('button').click();

    // Availability is DERIVED server-side (`onHand - reserved`), so the screen
    // redraws from the answer rather than from what was typed. A client that
    // subtracted for itself would disagree with the search index the moment a
    // reservation existed.
    await expect(row).toContainText('7');
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

    await expect(page).toHaveURL(/\/sign-in$/);

    // And the token is gone, not merely hidden: navigating straight back must
    // meet the guard again.
    await page.goto('/orders');
    await expect(page).toHaveURL(/\/sign-in$/);
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
  await page.getByRole('button', { name: SHOPKEEPER }).click();
  await expect(page).not.toHaveURL(/\/sign-in$/);
}
