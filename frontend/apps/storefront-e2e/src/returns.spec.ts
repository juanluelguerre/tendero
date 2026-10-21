import { expect, test, type Page } from '@playwright/test';
import { aProductInTheCart, backofficeUrl, checkoutWithAnAddress, payWith, signInToTheBackoffice } from './journeys';

/**
 * A return, the whole way round, in two applications: a shopper pays in the
 * storefront, the shopkeeper ships and delivers in the backoffice, and the
 * shopper sends something back from the order's own page — which is the only
 * way back a guest has, since the order's address is the credential.
 *
 * It is the journey the traffic simulator's `returner` walks (ADR 0037), and the
 * one no browser test walked before: every step of it had its own test, and the
 * screens between them had none. The refund itself is proved against a real
 * Postgres (`CheckoutLoopTests`); what only a browser can say is that the form a
 * delivered order offers is the one that opens a return.
 */

const BACKOFFICE = backofficeUrl;

/**
 * One move on one order's row, once the server offers it. An order is shippable
 * only after the outbox has held its stock, so the row is read again until the
 * move appears rather than assumed to be there.
 */
async function move(page: Page, orderId: string, label: RegExp): Promise<void> {
  // The id is the row's own button since the redesign (R-4.2), titled with the
  // whole id; and the list opens on what is to ship, so this asks for every order.
  const row = page.locator('tr', { has: page.locator(`button[title="${orderId}"]`) });

  await expect(async () => {
    await page.goto(`${BACKOFFICE}/orders?status=all`);
    await expect(row.getByRole('button', { name: label })).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });

  // The server's answer, not the button's absence: the row hides its moves while
  // one is on its way (`busy`), so the button was gone before the move was done,
  // and the next goto could abort it — the order stayed unshipped and *Delivered*
  // never appeared (one run in five, 2026-10-21).
  const answered = page.waitForResponse(
    (response) => response.url().includes(`/api/orders/${orderId}/move`) && response.request().method() === 'POST',
  );
  await row.getByRole('button', { name: label }).click();
  expect((await answered).ok()).toBe(true);
}

test.describe('a return', () => {
  test('is opened from the order page once the shopkeeper has delivered it', async ({ page, context }) => {
    // A checkout, a sign-in and two moves that each wait for the outbox, up to
    // thirty seconds apiece: under the default thirty for the whole test, the
    // first wait that took its time ended the test with it — in a whole run,
    // after the rest of the suite, and never alone.
    test.setTimeout(120_000);

    await aProductInTheCart(page);
    await checkoutWithAnAddress(page);
    await payWith(page, 'card-ok');
    await expect(page).toHaveURL(/\/en\/orders\//);
    const orderUrl = page.url();
    const orderId = orderUrl.split('/orders/')[1].split(/[?#]/)[0];

    // Not delivered yet: nothing to send back.
    await expect(page.getByRole('button', { name: 'Request the return' })).toHaveCount(0);

    const shopkeeper = await context.newPage();
    await signInToTheBackoffice(shopkeeper);
    await move(shopkeeper, orderId, /^ship$/i);
    await move(shopkeeper, orderId, /^delivered$/i);

    await page.goto(orderUrl);
    await page.locator('.returnable__pick input[type="checkbox"]').first().check();
    await page.locator('.returnable__detail select').first().selectOption('WrongSize');
    await page.getByRole('button', { name: 'Request the return' }).click();

    await expect(
      page.getByRole('status').filter({ hasText: 'Return opened. This page shows where it is.' }),
    ).toBeVisible();
  });
});
