import { expect, test, type Page } from '@playwright/test';
import { aProductInTheCart, checkoutWithAnAddress, payWith, signInToTheShop } from './journeys';

/**
 * The shopper's account in a browser (phase 15, docs/specs/shopper-account.md,
 * *Measures*): the three journeys the spec names — paying with a kept address and
 * a kept card, a default removed leaving checkout asking, a kept card declined
 * and said so — and the account removed.
 *
 * The rules are proved below this level (the aggregates' tests, the checkout
 * loop and `YourDataTests` against Postgres). What only a browser can say is that
 * the pages get a person there: the book's form saves, checkout shows what the
 * account remembers and asks when it should, the wallet's card is charged, and
 * removing the account ends with the right sentence.
 *
 * Every test leaves Ana's account as it found it, but the last, which removes it:
 * the next sign-in starts her again as a new customer, which is the behaviour it
 * proves.
 */

const book = (page: Page) => page.locator('[data-address-book]');
const wallet = (page: Page) => page.locator('[data-wallet]');

/** The book emptied, so each test starts from what it says it starts from. */
async function anEmptyBook(page: Page): Promise<void> {
  await page.goto('/en/account/addresses');
  await expect(page.locator('[data-address-count]')).toBeVisible();
  const entries = page.locator('[data-address]');
  // Counted down, not by the first one: `.first()` is asked again and finds the next.
  for (let left = await entries.count(); left > 0; left--) {
    await entries.first().getByRole('button', { name: 'Remove' }).click();
    await entries.first().locator('[data-confirm-remove]').click();
    await expect(entries).toHaveCount(left - 1);
  }
}

/** The wallet emptied the same way: each card forgotten at the provider by the page's own Remove. */
async function anEmptyWallet(page: Page): Promise<void> {
  await page.goto('/en/account/ways-to-pay');
  await expect(page.getByRole('heading', { level: 1, name: 'Ways to pay' })).toBeVisible();
  await expect(
    page.getByRole('button', { name: /add a card/i }).or(page.getByText(/your wallet is full/i)),
  ).toBeVisible();
  const methods = page.locator('[data-method]');
  for (let left = await methods.count(); left > 0; left--) {
    await methods.first().getByRole('button', { name: 'Remove' }).click();
    await methods.first().locator('[data-confirm-remove]').click();
    await expect(methods).toHaveCount(left - 1);
  }
}

async function keepAnAddress(page: Page, label: string, line1: string, defaults: boolean): Promise<void> {
  await page.goto('/en/account/addresses');
  await page.getByRole('link', { name: /add an address/i }).click();
  await page.locator("input[name='entry.label']").fill(label);
  await page.locator("input[name='address.line1']").fill(line1);
  await page.locator("input[name='address.postalCode']").fill('28013');
  await page.locator("input[name='address.city']").fill('Madrid');
  if (defaults) {
    await page.locator("input[name='entry.deliveryDefault']").check();
    await page.locator("input[name='entry.billingDefault']").check();
  }
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(book(page).locator(`[data-address='${label}']`)).toBeVisible();
}

async function keepACard(
  page: Page,
  card: 'card-ok' | 'card-declined',
  last4: string,
  makeDefault: boolean,
): Promise<void> {
  await page.goto('/en/account/ways-to-pay');
  await page.getByRole('button', { name: /add a card/i }).click();
  await page.locator(`input[name='card'][value='${card}']`).check();
  if (makeDefault) await page.getByLabel('Make it my default').check();
  await page.getByRole('button', { name: 'Save card' }).click();
  await expect(wallet(page).locator(`[data-method='${last4}']`)).toBeVisible();
}

test.describe('the shopper’s account', () => {
  test.beforeEach(async ({ page }) => {
    await signInToTheShop(page);
  });

  test('keeps an address, and checkout offers it preselected instead of a form to retype', async ({ page }) => {
    await anEmptyBook(page);
    await keepAnAddress(page, 'Home', 'Calle Mayor 12', true);
    await expect(book(page).locator("[data-address='Home']")).toContainText('Delivery default');

    await aProductInTheCart(page);
    await page.goto('/en/checkout');

    await expect(page.locator("[data-kept-addresses] [data-address='Home'] input")).toBeChecked();
    await expect(page.locator("input[name='address.line1']")).toHaveCount(0);

    await page.locator("input[name='shipping']").first().check();
    await payWith(page, 'card-ok');
    await expect(page).toHaveURL(/\/en\/orders\//);

    await anEmptyBook(page);
  });

  test('a default removed leaves no default, and the next checkout asks', async ({ page }) => {
    await anEmptyBook(page);
    await keepAnAddress(page, 'Home', 'Calle Mayor 12', true);
    await keepAnAddress(page, 'Work', 'Paseo de la Castellana 89', false);

    const home = book(page).locator("[data-address='Home']");
    await home.getByRole('button', { name: 'Remove' }).click();
    await expect(home.getByRole('alertdialog')).toContainText('no other address takes its place');
    await home.locator('[data-confirm-remove]').click();
    await expect(book(page).locator("[data-address='Work']")).not.toContainText('Delivery default');

    await aProductInTheCart(page);
    await page.goto('/en/checkout');

    await expect(page.getByText('Choose where to send it.')).toBeVisible();
    await expect(page.getByRole('button', { name: /^pay /i })).toBeDisabled();

    await page.locator("[data-kept-addresses] [data-address='Work'] input").check();
    await expect(page.locator("input[name='shipping']").first()).toBeVisible();

    await anEmptyBook(page);
  });

  test('pays with the default kept card, and a kept card declined is said so and stays chosen', async ({ page }) => {
    await anEmptyWallet(page);
    await keepACard(page, 'card-ok', '4242', true);
    await keepACard(page, 'card-declined', '0002', false);
    await expect(wallet(page).locator("[data-method='4242']")).toContainText('Default');

    // The default, charged by its id: no test card is chosen for it.
    await aProductInTheCart(page);
    await checkoutWithAnAddress(page);
    await expect(page.locator("[data-saved-cards] [data-card='4242'] input")).toBeChecked();
    await page.getByRole('button', { name: /^pay /i }).click();
    await expect(page).toHaveURL(/\/en\/orders\//);

    // The other card, declined: said on its row, still chosen, the shopper still at checkout.
    await aProductInTheCart(page);
    await checkoutWithAnAddress(page);
    await page.locator("[data-saved-cards] [data-card='0002'] input").check();
    await page.getByRole('button', { name: /^pay /i }).click();

    await expect(page.locator('[data-card-declined]')).toBeVisible();
    await expect(page.locator("[data-saved-cards] [data-card='0002'] input")).toBeChecked();
    await expect(page).toHaveURL(/\/en\/checkout/);

    await anEmptyWallet(page);
  });

  test('records a marketing consent with its date, and the menu leads to every section', async ({ page }) => {
    await page.goto('/en/account/preferences');

    const consent = page.locator('[data-marketing]');
    // The switch shows what the shop has recorded, so it turns once the server
    // answers, not on the click: click, then wait for the state — never check(),
    // which wants the change at the click.
    const toggle = consent.getByRole('switch');
    await expect(toggle).toBeEnabled();
    if (await toggle.isChecked()) {
      await toggle.click();
      await expect(toggle).not.toBeChecked();
    }
    await expect(consent).toContainText(/turned off on|off until you turn it on/i);

    await toggle.click();
    await expect(toggle).toBeChecked();
    await expect(consent).toContainText(/given on/i);
    await expect(consent).toContainText('The shop does not send email yet.');
    await toggle.click();
    await expect(toggle).not.toBeChecked();
    await expect(consent).toContainText(/turned off on/i);

    // The menu: every section is one click from every other.
    for (const [section, heading] of [
      ['addresses', 'Addresses'],
      ['waysToPay', 'Ways to pay'],
      ['yourData', 'Your data'],
      ['preferences', 'Preferences'],
    ] as const) {
      await page.locator(`storefront-account-nav [data-account-section='${section}']`).first().click();
      await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
    }
  });

  test('removes the account: forgotten now, the orders kept, and a way out of the session', async ({ page }) => {
    await page.goto('/en/account/your-data');

    await page.getByRole('button', { name: 'Remove my account' }).click();
    await expect(page.getByRole('alertdialog')).toContainText('This cannot be undone.');
    await page.locator('[data-confirm-remove-account]').click();

    const removed = page.locator('[data-account-removed]');
    await expect(removed).toContainText('Your account was removed.');
    await expect(removed).toContainText(/orders are kept for 6 years/i);
    await expect(removed.getByRole('button', { name: /sign out/i })).toBeVisible();
  });
});
