import { expect, test, type Page } from '@playwright/test';

/**
 * What a shopper does, in a browser.
 *
 * It waited on the product page, because the PDP changed the flow it would test:
 * before it existed the only path to the cart was the search card, which works
 * at one variant per product and would not at eight.
 *
 * Everything below this level already runs — 503 tests, five of which drive the
 * whole commerce loop against a real Postgres and the real outbox. So this file
 * asserts only what a browser can, and the four things it checks are the four
 * that could not be checked anywhere else:
 *
 * 1. **The URL carries the language.** `/es/…` and `/en/…` are different pages,
 *    and a bare path gets a language rather than a 404.
 * 2. **The code is the key, not the slug** (ADR 0026). A link with the wrong
 *    slug resolves and corrects itself, which under a slug-keyed URL would have
 *    been a 404.
 * 3. **The picker refuses what the shop does not sell.** A combination with no
 *    variant renders DISABLED rather than absent — the whole reason the endpoint
 *    returns every axis option instead of only the sold ones.
 * 4. **`hreflang` exists and names every language including its own.** A tag in
 *    `<head>` has no visual effect, so nothing but a test can see it.
 */

/**
 * Whatever the running stack has, found with a query the NDCG gate measures.
 *
 * A single letter does not work and should not: the shop refuses a query below
 * two characters rather than returning noise. The query comes from the golden
 * set, so if this stops finding anything the gate said so first.
 */
async function firstProduct(page: Page): Promise<{ href: string; name: string }> {
  await page.goto('/es');
  await page.getByRole('searchbox').fill('zapatillas');
  await page.getByRole('button', { name: /buscar|search/i }).click();

  const link = page.locator('a.card__link').first();
  await expect(link).toBeVisible();

  return {
    href: (await link.getAttribute('href')) ?? '',
    name: (await link.innerText()).trim(),
  };
}

test.describe('the storefront', () => {
  test('gives a URL with no language one, keeping the path', async ({ page }) => {
    await page.goto('/cart');

    // Every link that existed before P5-13 had no language in it, including the
    // order confirmations already in people's history. They land somewhere.
    await expect(page).toHaveURL(/\/(es|en)\/cart$/);
  });

  test('puts the language in the URL and changes it there', async ({ page }) => {
    await page.goto('/es');
    await expect(page).toHaveURL(/\/es$/);

    await page.getByRole('button', { name: 'en', exact: true }).click();

    // Not a signal and not localStorage: the address bar is the state, which is
    // what makes a language a thing two people can share a link to.
    await expect(page).toHaveURL(/\/en$/);
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  });

  test('reaches a product page from a search result', async ({ page }) => {
    const product = await firstProduct(page);

    await page.goto(product.href);

    await expect(page.getByRole('heading', { level: 1 })).toHaveText(product.name);
    await expect(page).toHaveURL(/\/es\/p\/[^/]+\/[0-9A-HJKMNP-TV-Z]{10}$/);
  });

  /**
   * The promise ADR 0026 rests on, and the one this suite exists to prove: the
   * slug is decoration. A stale link — a renamed product, a copied URL from
   * another language — still resolves, and the page corrects the address rather
   * than showing a 404.
   */
  test('resolves a link whose slug is wrong and corrects the address', async ({ page }) => {
    const product = await firstProduct(page);
    const code = product.href.split('/').pop();

    await page.goto(`/es/p/un-slug-que-ya-no-existe/${code}`);

    await expect(page.getByRole('heading', { level: 1 })).toHaveText(product.name);
    await expect(page).not.toHaveURL(/un-slug-que-ya-no-existe/);
  });

  test('answers a code nobody owns with a page that says so', async ({ page }) => {
    await page.goto('/es/p/cualquier-cosa/ZZZZZZZZZZ');

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.getByRole('link', { name: /seguir comprando|continue/i })).toBeVisible();
  });

  /**
   * A combination the shop does not sell renders DISABLED, not absent. Hiding it
   * would tell the shopper their size is not made; disabling it tells them the
   * truth — and it is why the API returns every option the catalogue defines
   * rather than only the ones with a variant.
   *
   * Skipped when the seeded catalogue has no product with a gap in its matrix,
   * because the fixture is whatever the running stack has — and on a fresh
   * import that is NO picker at all: the seed mints one `-DEFAULT` variant per
   * product and the matrix is declared from the backoffice (`P1-8`). The first
   * version asserted an option was visible before deciding whether to skip,
   * which turned a catalogue with no axes into a failure rather than a skip.
   */
  test('disables a combination it does not sell', async ({ page }) => {
    const product = await firstProduct(page);
    await page.goto(product.href);

    // Wait for the buy area, which every product has, before asking about the
    // picker, which only a product with declared axes has.
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(product.name);

    const options = page.locator('.option');
    test.skip(
      (await options.count()) === 0,
      'The first product in the catalogue has no variant axes declared.',
    );

    const disabled = page.locator('.option:disabled');
    test.skip(
      (await disabled.count()) === 0,
      'No product in the seeded catalogue has a hole in its variant matrix.',
    );

    // Still rendered, and still readable. An option that disappears makes the
    // picker jump and makes the shopper think the shop lost something.
    await expect(disabled.first()).toBeVisible();
    await expect(disabled.first()).toBeDisabled();
  });

  /**
   * Tags with no visual effect, which is exactly why they need a test. A page
   * must list ITSELF among its alternates — `hreflang` is a set of equivalents,
   * and a page missing from its own set is ignored rather than half-honoured.
   */
  test('declares a canonical URL and every language it exists in', async ({ page }) => {
    const product = await firstProduct(page);
    await page.goto(product.href);

    await expect(page.locator('link[rel="canonical"]')).toHaveCount(1);

    const alternates = page.locator('link[rel="alternate"][hreflang]');
    await expect(alternates).toHaveCount(3); // es, en, x-default

    for (const culture of ['es', 'en', 'x-default']) {
      await expect(
        page.locator(`link[rel="alternate"][hreflang="${culture}"]`),
      ).toHaveCount(1);
    }
  });

  /**
   * The whole point of the picker being on a page of its own: the shopper
   * chooses the variant, and the cart gets that SKU rather than whichever one
   * the search happened to match.
   */
  test('adds the chosen variant to the cart', async ({ page }) => {
    const product = await firstProduct(page);
    await page.goto(product.href);

    const add = page.getByRole('button', { name: /añadir al carrito|add to cart/i });
    const soldOut = page.locator('.buy__soldOut, .buy__unavailable');

    // Wait for the buy area to resolve to SOMETHING before asking which it is.
    // `isVisible()` is the one assertion here that does not auto-retry, so
    // asking it directly answers "no" while the answer is still in flight — and
    // a skip is worse than a failure, because it looks like a decision.
    await expect(add.or(soldOut).first()).toBeVisible();

    test.skip(!(await add.isVisible()), 'The first product in the catalogue is sold out.');

    await add.click();

    // The badge is the confirmation, and the page does not move: a shop that
    // threw you out of your results after every add is one you buy one thing
    // from.
    await expect(page.locator('.cart__badge')).toBeVisible();
    await expect(page).toHaveURL(/\/p\//);
  });
});
