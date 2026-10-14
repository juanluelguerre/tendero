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

  /**
   * What was asked lives in the address. Switching language goes to another
   * page (/es → /en), and a kit held only in the old page's memory came back
   * empty (the author, 2026-10-14).
   */
  test('keeps the kit, rebuilt in the new language, when the language is switched', async ({ page }) => {
    await page.goto('/es/kits/COFFEE_CORNER');
    await page.getByLabel('Tu presupuesto').fill('300');
    await page.getByRole('button', { name: 'Montar el kit' }).click();
    await expect(page).toHaveURL(/\/es\/kits\/COFFEE_CORNER\?budget=300$/);
    await expect(page.getByText(/de 300,00\s€/)).toBeVisible();

    await page
      .getByRole('banner')
      .getByRole('button', { name: /tu cuenta y tus preferencias/i })
      .click();
    await page.locator('#account-menu').getByRole('button', { name: 'en', exact: true }).click();

    await expect(page).toHaveURL(/\/en\/kits\/COFFEE_CORNER\?budget=300$/);
    await expect(page.getByLabel('Your budget')).toHaveValue('300');
    await expect(page.getByText(/of €300\.00 · €[\d.,]+ left/)).toBeVisible();
  });

  /**
   * A sentence that fills the form (K-6): the model names the kit, from the
   * script in CI, because no model fits a runner. Code reads the budget and
   * "induction", and the kit's page builds it with the form showing what was
   * understood. The sentence is also one the local model gets right
   * (`tools/kit-sentences`), so the spec holds on a stack with the real model;
   * "a kitchen kit for induction" is one it does not.
   */
  test('a sentence takes the shopper to the kit it names, with the form filled', async ({ page }) => {
    test.skip(!process.env['TINO_SCRIPTED'] && !process.env['CI'], 'needs the stack started with Tendero__TinoScript');

    await page.goto('/en/kits');
    await page
      .getByLabel('Tell us in your own words')
      .fill('everything I need to start cooking on induction, under €200');
    await page.getByRole('button', { name: 'Find my kit' }).click();

    await expect(page).toHaveURL(/\/en\/kits\/KITCHEN_STARTER\?budget=200&needs=INDUCTION(:|%3A)true$/);
    await expect(page.getByLabel('Your budget')).toHaveValue('200');
    await expect(page.getByRole('switch', { name: 'induction' })).toBeChecked();
    await expect(page.getByText(/of €200\.00 · €[\d.,]+ left/)).toBeVisible();
  });

  /** With no model the box is not drawn, and the list is the page it was (invariant 8). */
  test('offers no sentence box when the shop cannot read one', async ({ page }) => {
    const status = await (await page.request.get('/api/kits/understand/status')).json();
    test.skip(status.available === true, 'the stack has a model: the box is drawn');

    await page.goto('/en/kits');

    await expect(page.getByRole('heading', { level: 1, name: 'Kits' })).toBeVisible();
    await expect(page.getByLabel('Tell us in your own words')).toHaveCount(0);
  });

  test('a budget that is not a number builds nothing', async ({ page }) => {
    await page.goto('/en/kits/KITCHEN_STARTER');

    await page.getByLabel('Your budget').fill('a lot');

    await expect(page.getByRole('button', { name: 'Build the kit' })).toBeDisabled();
  });
});
