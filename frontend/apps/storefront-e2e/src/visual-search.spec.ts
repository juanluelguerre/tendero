import { expect, type Page, type Route, test } from '@playwright/test';

/**
 * Searching with a picture, in a browser (phase 13a, *I-6*; the camera search,
 * CS-1 to CS-9).
 *
 * The stack this runs against has no picture model — none fits a runner, and a
 * default start has none either — so the first test is the real thing: **the
 * degraded path**, a search box that is exactly the one it always was
 * (invariant 8).
 *
 * The others answer the shop's visual endpoints from here, with cards the
 * running shop itself returned for a search by words. What they prove is the
 * wiring — the camera, the panel, the picture sheet, the objects' marks and
 * blocks, the words that narrow them, the line of what is not sold, a block
 * that fails, the camera's fallback — not a model's judgement, which
 * `tools/SearchEval --suite objects` measures.
 */
test.describe('searching with a picture', () => {
  // The smallest PNG there is: one pixel. What is sent is not what is under test.
  const picture = {
    name: 'scene.png',
    mimeType: 'image/png',
    buffer: Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
      'base64',
    ),
  };

  /** The author's photograph with a backpack, as the detector reads it: two objects the shop sells and one it does not. */
  const scene = {
    width: 600,
    height: 800,
    objects: [
      {
        number: 1,
        kind: 'BACKPACK',
        sold: true,
        name: 'Mochilas',
        box: { left: 30, top: 110, width: 230, height: 530 },
        score: 0.71,
      },
      {
        number: 2,
        kind: 'SHIRT',
        sold: true,
        name: 'Camisetas',
        box: { left: 120, top: 0, width: 280, height: 530 },
        score: 0.55,
      },
      {
        number: 3,
        kind: 'watch',
        sold: false,
        name: 'reloj',
        box: { left: 365, top: 432, width: 45, height: 40 },
        score: 0.48,
      },
    ],
  };

  type Hit = { productId: string; name: string; imageId: string | null };

  // The box is drawn twice, the header's and the phone's; the one showing is the one a shopper has.
  const camera = (page: Page) => page.locator('[data-visual-search-door]:visible');
  const panel = (page: Page) => page.locator('storefront-visual-search-panel');
  const sheet = (page: Page) => page.locator('storefront-picture-sheet dialog');

  async function open(page: Page) {
    await page.goto('/es');
    const refuse = page.getByRole('button', { name: /no permitir|solo las necesarias|rechazar/i });
    if (await refuse.isVisible().catch(() => false)) await refuse.click();
  }

  /** The shop says what it can do with a picture, whatever the stack behind it has. */
  const shopCan = (page: Page, objects = true) =>
    page.route('**/api/search/by-image/status', (route) => route.fulfill({ json: { available: true, objects } }));

  /** Real cards, from the running shop: what a stubbed answer names must be products it can draw. */
  async function cards(request: import('@playwright/test').APIRequestContext, words: string): Promise<Hit[]> {
    const found = await (await request.get(`/api/search?q=${encodeURIComponent(words)}&culture=es`)).json();
    const hits = (found.hits as Hit[]).slice(0, 6);
    expect(hits.length).toBeGreaterThan(0);
    return hits;
  }

  const answer = (hits: Hit[]) => ({
    json: {
      items: hits.map((product, index) => ({ product, picture: product.imageId ?? '', similarity: 0.7 - index / 20 })),
    },
  });

  /** Each crop answered by its category: what the browser asked is the request's own words. */
  async function answerCrops(page: Page, byCategory: Record<string, (route: Route) => Promise<void>>) {
    await page.route('**/api/search/by-image?**', async (route) => {
      const category = new URL(route.request().url()).searchParams.get('category') ?? '';
      const respond = byCategory[category] ?? ((r: Route) => r.fulfill({ json: { items: [] } }));
      await respond(route);
    });
  }

  async function givePicture(page: Page) {
    await camera(page).click();
    await panel(page).locator('[data-visual-search-file]').setInputFiles(picture);
    await expect(sheet(page)).toBeVisible();
  }

  test('a shop without visual search has the search box it always had, and searches by words', async ({ page }) => {
    // Said by the test, as `shopCan` says the opposite: a stack started with
    // its eyes on answers available, and this is about one that does not.
    await page.route('**/api/search/by-image/status', (route) =>
      route.fulfill({ json: { available: false, objects: false } }),
    );
    await open(page);

    await expect(camera(page)).toHaveCount(0);

    const box = page.getByRole('searchbox').first();
    await box.fill('mochila');
    await box.press('Enter');
    await expect(page.locator('storefront-product-card').first()).toBeVisible();
  });

  test('a picture is answered with a block an object, its numbered marks, and the line of what is not sold', async ({
    page,
    request,
  }) => {
    const backpacks = await cards(request, 'mochila');
    const shirts = await cards(request, 'camiseta');
    await shopCan(page);
    let objectsSent: string | null = null;
    await page.route('**/api/search/by-image/objects?**', async (route) => {
      objectsSent = route.request().headers()['content-type'] ?? null;
      await route.fulfill({ json: scene });
    });
    await answerCrops(page, {
      BACKPACK: (route) => route.fulfill(answer(backpacks)),
      SHIRT: (route) => route.fulfill(answer(shirts)),
    });

    await open(page);
    await givePicture(page);
    // Before it is sent: the whole picture, or a part of it (CS-1.2).
    await expect(sheet(page).getByRole('radio', { name: /la imagen entera/i })).toBeChecked();
    await sheet(page).locator('[data-picture-search]').click();

    await expect(sheet(page).locator('[data-picture-block="1"]')).toContainText('Mochilas');
    await expect(sheet(page).locator('[data-picture-block="2"]')).toContainText('Camisetas');
    expect(objectsSent).toMatch(/^multipart\/form-data/);
    await expect(sheet(page).locator('storefront-object-block').first().locator('storefront-product-card')).toHaveCount(
      Math.min(4, backpacks.length),
    );
    // The watch has a mark that says only its number, and no block: the line names it.
    await expect(sheet(page).locator('[data-picture-mark]')).toHaveCount(3);
    await expect(sheet(page).locator('[data-picture-block="3"]')).toHaveCount(0);
    await expect(sheet(page).locator('[data-picture-not-sold]')).toContainText(/no están en la tienda: reloj/i);
    // A mark is a button named by its number and kind (CS-8).
    await expect(sheet(page).getByRole('button', { name: '1 · Mochilas' })).toBeVisible();

    // See all: that block's whole list, on the page of its own.
    await sheet(page).locator('[data-picture-see-all="2"]').click();
    await expect(page).toHaveURL(/\/es\/search\/picture$/);
    await expect(sheet(page)).toHaveCount(0);
    await expect(page.locator('storefront-product-card')).toHaveCount(shirts.length);
    await page.locator('storefront-product-card a').first().click();
    await expect(page).toHaveURL(/\/es\/p\//);
  });

  test('words narrow every block within its category, and a block they empty says so', async ({ page, request }) => {
    const backpacks = await cards(request, 'mochila');
    await shopCan(page);
    await page.route('**/api/search/by-image/objects?**', (route) => route.fulfill({ json: scene }));
    const asked: (string | null)[] = [];
    await answerCrops(page, {
      BACKPACK: (route) => {
        asked.push(new URL(route.request().url()).searchParams.get('q'));
        return route.fulfill(answer(backpacks));
      },
      SHIRT: (route) => {
        const words = new URL(route.request().url()).searchParams.get('q');
        return route.fulfill(words ? { json: { items: [] } } : answer(backpacks));
      },
    });

    await open(page);
    await givePicture(page);
    await sheet(page).locator('[data-picture-search]').click();
    await expect(sheet(page).locator('[data-picture-block="2"]')).toBeVisible();

    const words = sheet(page).getByRole('searchbox');
    await words.fill('gris');
    await words.press('Enter');

    await expect(sheet(page)).toContainText(/nada de esto con «gris»/i);
    expect(asked).toEqual([null, 'gris']);
    // The line of what is not sold does not move with the words.
    await expect(sheet(page).locator('[data-picture-not-sold]')).toContainText(/reloj/i);

    await sheet(page)
      .getByRole('button', { name: /verlo sin «gris»/i })
      .click();
    await expect(sheet(page).locator('[data-picture-block="2"]').locator('xpath=ancestor::section')).toContainText(
      /parecidos en la tienda/i,
    );
  });

  test('a part marked, moved with the keyboard, is what is searched', async ({ page, request }) => {
    const backpacks = await cards(request, 'mochila');
    await shopCan(page);
    await page.route('**/api/search/by-image/objects?**', (route) =>
      route.fulfill({
        json: {
          width: 300,
          height: 400,
          objects: [{ ...scene.objects[0], box: { left: 10, top: 10, width: 200, height: 300 } }],
        },
      }),
    );
    await answerCrops(page, { BACKPACK: (route) => route.fulfill(answer(backpacks)) });

    await open(page);
    await givePicture(page);
    await sheet(page).locator('[data-picture-choose-part]').check();
    const part = sheet(page).locator('[data-picture-part]');
    await expect(part).toBeVisible();
    await part.focus();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Shift+ArrowDown');
    await sheet(page).locator('[data-picture-search]').click();

    await expect(sheet(page).locator('[data-picture-block="1"]')).toContainText('Mochilas');
    await expect(sheet(page).locator('[data-picture-mark]')).toHaveCount(1);
  });

  test.describe('when a part is missing (CS-9)', () => {
    test('without the detector, the picture is one object, searched whole by likeness', async ({ page, request }) => {
      const backpacks = await cards(request, 'mochila');
      await shopCan(page, false);
      let objectsAsked = false;
      await page.route('**/api/search/by-image/objects?**', (route) => {
        objectsAsked = true;
        return route.abort();
      });
      await answerCrops(page, { '': (route) => route.fulfill(answer(backpacks)) });

      await open(page);
      await givePicture(page);
      await sheet(page).locator('[data-picture-search]').click();

      await expect(sheet(page).locator('[data-picture-block="1"]')).toHaveText(/tu imagen/i);
      await expect(sheet(page).locator('storefront-product-card')).toHaveCount(Math.min(4, backpacks.length));
      await expect(sheet(page).locator('[data-picture-mark]')).toHaveCount(0);
      expect(objectsAsked).toBe(false);
    });

    test('a block whose search fails says so, the others stand, and it can be asked again alone', async ({
      page,
      request,
    }) => {
      const backpacks = await cards(request, 'mochila');
      await shopCan(page);
      await page.route('**/api/search/by-image/objects?**', (route) => route.fulfill({ json: scene }));
      let shirtAsked = 0;
      await answerCrops(page, {
        BACKPACK: (route) => route.fulfill(answer(backpacks)),
        SHIRT: (route) =>
          ++shirtAsked === 1 ? route.fulfill({ status: 500, body: 'down' }) : route.fulfill(answer(backpacks)),
      });

      await open(page);
      await givePicture(page);
      await sheet(page).locator('[data-picture-search]').click();

      await expect(sheet(page).getByRole('alert')).toContainText(/no hemos podido buscar este/i);
      await expect(
        sheet(page).locator('storefront-object-block').first().locator('storefront-product-card').first(),
      ).toBeVisible();

      await sheet(page)
        .getByRole('button', { name: /volver a intentarlo/i })
        .click();
      await expect(sheet(page).getByRole('alert')).toHaveCount(0);
      expect(shirtAsked).toBe(2);
    });

    test('a file the shop does not take is said in the sheet, with the way to give another', async ({ page }) => {
      await shopCan(page);
      await page.route('**/api/search/by-image/objects?**', (route) =>
        route.fulfill({ status: 400, json: { code: 'PictureNotTaken', status: 400, title: 'not a picture' } }),
      );

      await open(page);
      await givePicture(page);
      await sheet(page).locator('[data-picture-search]').click();

      await expect(sheet(page).getByRole('alert')).toContainText(/no es una imagen que podamos leer/i);
      await sheet(page)
        .getByRole('button', { name: /otra imagen/i })
        .click();
      await expect(sheet(page)).toHaveCount(0);
      await expect(panel(page).locator('[data-visual-search-choose]')).toBeVisible();
    });
  });

  test('a page of results opened without a picture asks for one', async ({ page }) => {
    await page.goto('/es/search/picture');

    await expect(page.getByText(/dale a la tienda una imagen/i)).toBeVisible();
  });
});
