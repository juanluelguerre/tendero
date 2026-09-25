import { expect, test, type Page } from '@playwright/test';

/**
 * Paying, and not paying — the journeys the traffic simulator walks (ADR 0037)
 * and that no browser test walked before it: a card that works, a card the bank
 * declines, and a checkout left half way.
 *
 * The money path itself is proved below this level (the checkout loop against a
 * real Postgres). What only a browser can say is that the screens get a person
 * there: the form, the delivery options that appear once there is an address,
 * the card they chose, and where each answer leaves them. The fake payment
 * provider's cards are named in the page itself, which is why these tests can
 * choose the outcome.
 */

/** A product the shop can sell now: the first search result whose page offers the button. */
async function aProductInTheCart(page: Page): Promise<void> {
  await page.goto('/en?q=backpack');
  await expect(page.getByRole('status').filter({ hasText: /backpack/i })).toBeVisible();

  const links = page.locator('.results-meta + .grid a.card__link');
  const count = await links.count();

  for (let i = 0; i < count; i++) {
    await page.goto((await links.nth(i).getAttribute('href')) ?? '');
    const add = page.locator('main button', { hasText: /add to cart/i }).first();
    const soldOut = page.locator('.buy__soldOut, .buy__unavailable');

    // Wait for the buy area to resolve before asking which it is: isVisible()
    // does not retry (see shopper.spec.ts, 'adds the chosen variant').
    await expect(add.or(soldOut).first()).toBeVisible();
    if (await add.isVisible()) {
      await add.click();
      await expect(page.locator('.cart__badge')).toBeVisible();
      return;
    }

    await page.goBack();
  }

  test.skip(true, 'Nothing among the backpacks is in stock.');
}

async function checkoutWithAnAddress(page: Page): Promise<void> {
  await page.goto('/en/checkout');
  await page.locator("input[name='recipientName']").fill('Ana Test');
  await page.locator("input[name='line1']").fill('Calle Mayor 1');
  await page.locator("input[name='postalCode']").fill('28013');
  await page.locator("input[name='city']").fill('Madrid');
  await page.locator("input[name='city']").press('Tab');

  // The delivery options arrive once there is somewhere to deliver to.
  await page.locator("input[name='shipping']").first().check();
}

async function payWith(page: Page, card: 'card-ok' | 'card-declined'): Promise<void> {
  await page.locator(`input[name='instrument'][value='${card}']`).check();
  await page.getByRole('button', { name: /^pay /i }).click();
}

test.describe('the checkout', () => {
  test('pays with a card that works and lands on the order', async ({ page }) => {
    await aProductInTheCart(page);
    await checkoutWithAnAddress(page);

    await payWith(page, 'card-ok');

    await expect(page).toHaveURL(/\/en\/orders\//);
    await expect(page.getByText('Thank you. Your order is placed.')).toBeVisible();
  });

  test('a declined card keeps the shopper at the checkout, with the cart as it was', async ({ page }) => {
    await aProductInTheCart(page);
    await checkoutWithAnAddress(page);

    await payWith(page, 'card-declined');

    await expect(page.getByRole('alert')).toHaveText('The bank declined the card. Try another one.');
    await expect(page).toHaveURL(/\/en\/checkout/);
    await expect(page.locator('.cart__badge')).toBeVisible();
  });

  /**
   * Abandonment, as a shop sees it: nothing happens. The person leaves, and the
   * cart is still theirs when they come back — which is what makes a reminder,
   * or a saved cart, possible at all.
   */
  test('a checkout left half way keeps the cart for when the shopper comes back', async ({ page }) => {
    await aProductInTheCart(page);
    await page.goto('/en/checkout');
    await page.locator("input[name='recipientName']").fill('Ana Test');

    await page.goto('/en');
    await page.reload();

    await expect(page.locator('.cart__badge')).toBeVisible();
    await page.goto('/en/cart');
    await expect(page.getByRole('heading', { name: 'Your cart' })).toBeVisible();
    await expect(page.getByText('1 item')).toBeVisible();
  });
});
