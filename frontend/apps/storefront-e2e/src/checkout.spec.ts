import { expect, test } from '@playwright/test';
import { aProductInTheCart, checkoutWithAnAddress, payWith } from './journeys';

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
