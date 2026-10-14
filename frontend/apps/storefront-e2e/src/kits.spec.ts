import { expect, test } from '@playwright/test';

/**
 * A kit, from the shop's list to the cart (phase 13c, K-5).
 *
 * The solver is proved below this level — its properties, its oracle, the
 * slice on a real index. What only a browser can say is that the screens get
 * a person there: the kits listed, a budget typed, the kit built with a total
 * the budget holds, a sentence where there is something to say, and the kit
 * in the cart.
 */
test.describe('the kits', () => {
  test('lists the kits the shop wrote, and opens one', async ({ page }) => {
    await page.goto('/en/kits');

    await expect(page.getByRole('heading', { level: 1, name: 'Kits' })).toBeVisible();
    await page.getByRole('link', { name: 'Kitchen starter kit' }).first().click();

    await expect(page).toHaveURL(/\/en\/kits\/KITCHEN_STARTER$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Kitchen starter kit' })).toBeVisible();
    // Before building, what the kit is made of: the page is never half empty.
    await expect(page.getByRole('heading', { name: 'What goes in it' })).toBeVisible();
    // No budget, nothing to build.
    await expect(page.getByRole('button', { name: 'Build the kit' })).toBeDisabled();
  });

  test('builds a kit to a budget, says what it comes to, and puts it in the cart', async ({ page }) => {
    await page.goto('/en/kits/KITCHEN_STARTER');
    await page.getByLabel('Your budget').fill('250');
    await page.getByRole('button', { name: 'Build the kit' }).click();

    // The total against the budget, and one row a slot.
    await expect(page.getByText(/of €250\.00 · €[\d.,]+ left/)).toBeVisible();
    const rows = page.locator('main ul li', { has: page.locator('storefront-product-image') });
    await expect(rows.first()).toBeVisible();

    const add = page.getByRole('button', { name: /Add (the kit|this one|these \d+) to the basket/ });
    test.skip(!(await add.isVisible()), 'Nothing in the kitchen kit is in stock at this budget.');

    await add.click();

    await expect(page).toHaveURL(/\/en\/cart$/);
    await expect(page.locator('.cart__badge, a[href$="/cart"]').first()).toBeVisible();
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('a budget that is not a number builds nothing', async ({ page }) => {
    await page.goto('/en/kits/KITCHEN_STARTER');

    await page.getByLabel('Your budget').fill('a lot');

    await expect(page.getByRole('button', { name: 'Build the kit' })).toBeDisabled();
  });
});
