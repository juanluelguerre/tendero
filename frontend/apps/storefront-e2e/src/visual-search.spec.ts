import { expect, type Page, test } from '@playwright/test';

/**
 * Searching with a picture, in a browser (phase 13a; the images spec, *I-6*).
 *
 * The stack this runs against has no picture model — none fits a runner, and a
 * default start has none either — so the first test is the real thing: **the
 * degraded path**, a search box that is exactly the one it always was
 * (invariant 8).
 *
 * The others answer the shop's two visual endpoints from here, with cards the
 * running shop itself returned for a search by words. What they prove is the
 * wiring — the camera, the panel, the picture sent, the page of the nearest
 * things, a refusal said where the picture was given — not a model's judgement,
 * which `tools/SearchEval --siglip` measures.
 */
test.describe('searching with a picture', () => {
  // The smallest PNG there is: one pixel. What is sent is not what is under test.
  const picture = {
    name: 'lamp.png',
    mimeType: 'image/png',
    buffer: Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
      'base64',
    ),
  };

  // The box is drawn twice, the header's and the phone's; the one showing is the one a shopper has.
  const camera = (page: Page) => page.locator('[data-visual-search-door]:visible');
  const panel = (page: Page) => page.locator('storefront-visual-search-panel');

  async function open(page: Page) {
    await page.goto('/es');
    const refuse = page.getByRole('button', { name: /no permitir|solo las necesarias|rechazar/i });
    if (await refuse.isVisible().catch(() => false)) await refuse.click();
  }

  /** The shop says it can be searched with a picture, whatever the stack behind it has. */
  const available = (page: Page) =>
    page.route('**/api/search/by-image/status', (route) => route.fulfill({ json: { available: true } }));

  test('a shop without visual search has the search box it always had, and searches by words', async ({ page }) => {
    const status = page.waitForResponse('**/api/search/by-image/status');
    await open(page);
    expect((await (await status).json()).available).toBe(false);

    await expect(camera(page)).toHaveCount(0);

    const box = page.getByRole('searchbox').first();
    await box.fill('mochila');
    await box.press('Enter');
    await expect(page.locator('storefront-product-card').first()).toBeVisible();
  });

  test('a picture chosen is sent, and the nearest things come back on a page of their own', async ({
    page,
    request,
  }) => {
    // Real cards, from the running shop: what the stubbed answer names must be products it can draw.
    const found = await (await request.get('/api/search?q=mochila&culture=es')).json();
    const hits = (found.hits as { productId: string; name: string; imageId: string | null }[]).slice(0, 3);
    expect(hits.length).toBeGreaterThan(0);

    await available(page);
    let sent: string | null = null;
    await page.route('**/api/search/by-image?**', async (route) => {
      sent = route.request().headers()['content-type'] ?? null;
      await route.fulfill({
        json: {
          items: hits.map((product, index) => ({
            product,
            picture: product.imageId ?? '',
            similarity: 0.9 - index / 10,
          })),
        },
      });
    });

    await open(page);
    await expect(camera(page)).toBeVisible();
    await camera(page).click();
    await expect(camera(page)).toHaveAttribute('aria-expanded', 'true');
    // The one sentence that is always there.
    await expect(panel(page)).toContainText(/no se guarda/i);

    await panel(page).locator('[data-visual-search-file]').setInputFiles(picture);

    await expect(page).toHaveURL(/\/es\/search\/picture$/);
    expect(sent).toMatch(/^multipart\/form-data/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(/lo más parecido que tenemos/i);
    await expect(page.locator('storefront-product-card')).toHaveCount(hits.length);
    await expect(page.locator('storefront-product-card').first()).toContainText(hits[0].name);
    await expect(panel(page)).toHaveCount(0);

    // And a card is a way into the shop like any other.
    await page.locator('storefront-product-card a').first().click();
    await expect(page).toHaveURL(/\/es\/p\//);
  });

  test('a file the shop does not take is said beside it, with the way to try another still there', async ({ page }) => {
    await available(page);
    await page.route('**/api/search/by-image?**', (route) =>
      route.fulfill({
        status: 400,
        json: { title: 'The request is not valid.', errors: { picture: ['not a picture'] } },
      }),
    );

    await open(page);
    await camera(page).click();
    await panel(page).locator('[data-visual-search-file]').setInputFiles(picture);

    await expect(panel(page).getByRole('alert')).toContainText(/no es una imagen que podamos leer/i);
    await expect(panel(page).locator('[data-visual-search-choose]')).toBeVisible();
    await expect(page).toHaveURL(/\/es$/);

    // Escape puts it away.
    await page.keyboard.press('Escape');
    await expect(panel(page)).toHaveCount(0);
  });

  test('a page of results opened without a picture asks for one', async ({ page }) => {
    await page.goto('/es/search/picture');

    await expect(page.getByText(/dale a la tienda una imagen/i)).toBeVisible();
  });
});
