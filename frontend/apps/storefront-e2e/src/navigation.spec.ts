import { expect, test } from '@playwright/test';

/**
 * R-0's success criterion for S1, held by the browser (the redesign's R-5.1):
 * a category is at most two interactions from any page, on a phone and on a
 * desktop. Before the redesign it could not be done from the header at all
 * (R-1, S-F1): the departments were cards on the home page and nowhere else.
 */
for (const [device, width, height] of [['a desktop', 1440, 900], ['a phone', 390, 844]] as const) {
  test(`a category is two interactions from the cart, on ${device}`, async ({ page }) => {
    await page.setViewportSize({ width, height });
    await page.goto('/es/cart');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    // One: the menu. Two: the category.
    await page.getByRole('button', { name: /^\s*Tienda\s*$/ }).click();
    await page.getByRole('link', { name: /^\s*Mochilas\s*$/ }).click();

    await expect(page).toHaveURL(/\/es\/c\/BACKPACK/);
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Mochilas');
  });
}

test('the menu marks the department the shopper is in, and closes on Esc', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/es/c/BACKPACK');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  const menu = page.getByRole('button', { name: /^\s*Tienda\s*$/ });
  await menu.click();
  await expect(menu).toHaveAttribute('aria-expanded', 'true');
  await expect(page.locator('#shop-menu [aria-current="true"]')).toContainText('Bolsas y mochilas');

  await page.keyboard.press('Escape');
  await expect(menu).toHaveAttribute('aria-expanded', 'false');
  await expect(menu).toBeFocused();
});
