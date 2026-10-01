import { expect, test, type Page } from '@playwright/test';

/**
 * The steps a shopper's journeys share: a product in the cart, the checkout
 * form, and a card. Used by checkout.spec.ts and returns.spec.ts — the journeys
 * the traffic simulator walks (ADR 0037).
 */

/** A product the shop can sell now: the first search result whose page offers the button. */
export async function aProductInTheCart(page: Page): Promise<void> {
  await page.goto('/en?q=backpack');
  await expect(page.getByRole('status').filter({ hasText: /backpack/i })).toBeVisible();

  const links = page.locator('.listing__grid a.card__link');
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

export async function checkoutWithAnAddress(page: Page): Promise<void> {
  await page.goto('/en/checkout');
  await page.locator("input[name='address.recipientName']").fill('Ana Test');
  await page.locator("input[name='address.line1']").fill('Calle Mayor 1');
  await page.locator("input[name='address.postalCode']").fill('28013');
  await page.locator("input[name='address.city']").fill('Madrid');
  await page.locator("input[name='address.city']").press('Tab');

  // The delivery options arrive once there is somewhere to deliver to.
  await page.locator("input[name='shipping']").first().check();
}

export async function payWith(page: Page, card: 'card-ok' | 'card-declined'): Promise<void> {
  await page.locator(`input[name='instrument'][value='${card}']`).check();
  await page.getByRole('button', { name: /^pay /i }).click();
}

export const backofficeUrl = process.env['BACKOFFICE_URL'] ?? 'http://localhost:4201';

/**
 * The seeded shopkeeper, signed in to the backoffice: see `shopkeeper.spec.ts` in
 * backoffice-e2e for why both issuers are handled. Shared by the return journey
 * and the category campaign (R-8), which both need the shopkeeper's hand.
 */
export async function signInToTheBackoffice(page: Page): Promise<void> {
  await page.goto(`${backofficeUrl}/sign-in`);
  await page.getByRole('button', { name: /continue/i }).click();

  if (/\/realms\//.test(page.url())) {
    await page.locator('input[name="username"]').fill('juanlu');
    await page.locator('input[name="password"]').fill('juanlu');
    await page.locator('input[type="submit"], button[type="submit"]').first().click();
  } else {
    await page.getByRole('link', { name: 'Juan Luis' }).click();
  }

  // In once the bar offers the account menu, which holds the way out since the redesign (R-3).
  await expect(page.getByRole('banner').getByRole('button', { name: /^(account|cuenta):/i })).toBeVisible();
}

/**
 * A shopper signed in to the storefront from *Tu cuenta*: the seeded Ana, by the
 * issuer's own page — the development issuer's link or Keycloak's form.
 */
export async function signInToTheShop(page: Page): Promise<void> {
  await page.goto('/es/account');
  await page.getByRole('button', { name: 'Identifícate' }).click();

  if (/\/realms\//.test(page.url())) {
    await page.locator('input[name="username"]').fill('ana');
    await page.locator('input[name="password"]').fill('ana');
    await page.locator('input[type="submit"], button[type="submit"]').first().click();
  } else {
    await page.getByRole('link', { name: 'Ana Ruiz' }).click();
  }

  await expect(page.getByRole('heading', { level: 1, name: 'Ana Ruiz' })).toBeVisible();
}
