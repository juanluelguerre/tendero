import { expect, test } from '@playwright/test';

/**
 * R-0's success criterion for S1, held by the browser (the redesign's R-5.1):
 * a category is at most two interactions from any page, on a phone and on a
 * desktop. Before the redesign it could not be done from the header at all
 * (R-1, S-F1): the departments were cards on the home page and nowhere else.
 *
 * R-7.3's F-0 made a department ONE click on a desktop: the departments have a
 * visible row, each a link, and its sections are the toggle beside it. The
 * phone keeps TIENDA and its drawer.
 */
test('a department is one click from the cart, on a desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/es/cart');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  await page.getByRole('navigation', { name: 'Departamentos' }).getByRole('link', { name: /^\s*Bolsas y mochilas\s*$/ }).click();

  await expect(page).toHaveURL(/\/es\/c\/BAGS/);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Bolsas y mochilas');
});

test('a category is two interactions from the cart, on a desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/es/cart');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  // One: the department's sections. Two: the category.
  await page.getByRole('button', { name: /Bolsas y mochilas: sus secciones/ }).click();
  await page.locator('#shop-menu').getByRole('link', { name: /^\s*Mochilas\s*$/ }).click();

  await expect(page).toHaveURL(/\/es\/c\/BACKPACK/);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Mochilas');
});

test('a category is two interactions from the cart, on a phone', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/es/cart');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  // One: the drawer. Two: the category.
  await page.getByRole('button', { name: /^\s*Tienda\s*$/ }).click();
  await page.getByRole('link', { name: /^\s*Mochilas\s*$/ }).click();

  await expect(page).toHaveURL(/\/es\/c\/BACKPACK/);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Mochilas');
});

test('the row marks the department the shopper is in, one panel at a time, closing on Esc', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/es/c/BACKPACK');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  const row = page.getByRole('navigation', { name: 'Departamentos' });
  await expect(row.locator('[aria-current="true"]')).toContainText('Bolsas y mochilas');
  await expect(page.getByRole('button', { name: /^\s*Tienda\s*$/ })).toBeHidden();

  const bags = page.getByRole('button', { name: /Bolsas y mochilas: sus secciones/ });
  const clothing = page.getByRole('button', { name: /Ropa: sus secciones/ });
  await bags.click();
  await expect(bags).toHaveAttribute('aria-expanded', 'true');
  await expect(page.locator('#shop-menu')).toContainText('Mochilas');
  await expect(page.locator('#shop-menu')).not.toContainText('Calzado');

  // Another department's toggle takes the panel, rather than opening a second.
  await clothing.click();
  await expect(bags).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('#shop-menu')).toContainText('Calzado');

  await page.keyboard.press('Escape');
  await expect(clothing).toHaveAttribute('aria-expanded', 'false');
  await expect(clothing).toBeFocused();
});
