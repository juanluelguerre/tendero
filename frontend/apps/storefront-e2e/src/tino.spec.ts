import { expect, test } from '@playwright/test';

/**
 * Tino, in a browser (phase 12b; the Tino spec, *Evaluation and tests*): the door
 * in the header, a question, the shop's card with a sentence and the chip of the
 * fact it cites, an answer about the shop, the notice a guest gets about orders —
 * and, when Tino does not answer, the search's results for the same words
 * (invariant 8).
 *
 * The stack answers from a script (`Tendero__TinoScript`, CI sets it): no model
 * and no scorer fit a runner, and what this proves is the wiring — the door, the
 * panel, the turn, the contexts' reads, the cards read by code — not a model's
 * judgement, which the `assistant` suite measures.
 */
test.describe('Tino', () => {
  test.skip(!process.env['TINO_SCRIPTED'] && !process.env['CI'], 'needs the stack started with Tendero__TinoScript');

  test.beforeEach(async ({ page }) => {
    await page.goto('/es');
    // Analytics are the shopper's to refuse; the door works either way.
    const refuse = page.getByRole('button', { name: /no permitir|solo las necesarias|rechazar/i });
    if (await refuse.isVisible().catch(() => false)) await refuse.click();
  });

  const door = (page: import('@playwright/test').Page) => page.locator('[data-tino-door]');
  const panel = (page: import('@playwright/test').Page) => page.locator('storefront-tino-panel');

  async function ask(page: import('@playwright/test').Page, question: string) {
    const composer = page.locator('#tino-question');
    await composer.fill(question);
    await composer.press('Enter');
  }

  test('answers with the shop’s card, a sentence and the chip of the fact it cites', async ({ page }) => {
    await expect(door(page)).toHaveText(/pregunta a tino/i);
    await door(page).click();
    await expect(door(page)).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#tino-question')).toBeFocused();

    // Docked, the page under the header is what scrolls, at the panel's left: the
    // awning keeps its height (a flex column once shrank it to nothing), and the
    // window has no scrollbar of its own.
    expect((await page.locator('hr.awning').boundingBox())?.height).toBeGreaterThan(0);
    expect(await page.evaluate(() => document.documentElement.scrollHeight <= window.innerHeight)).toBe(true);
    const edges = await page.evaluate(() => ({
      page: document.querySelector('storefront-shell [data-page]')!.getBoundingClientRect().right,
      panel: document.querySelector('storefront-tino-dock aside')!.getBoundingClientRect().left,
    }));
    expect(edges.page).toBe(edges.panel);

    await ask(page, 'una sartén que valga para inducción');

    const answer = panel(page).locator('[data-kind="Products"]');
    await expect(answer).toBeVisible({ timeout: 30_000 });
    // The card is the shop's: its name links to the product's own page.
    await expect(answer.locator('storefront-product-card a.card__link')).toHaveAttribute('href', /\/es\/p\//);
    await expect(answer.getByText('Es lo que la tienda tiene anotado de ella.')).toBeVisible();

    // The chip says where the sentence comes from, and opens the fact itself.
    const chip = answer.locator('storefront-tino-source summary');
    await expect(chip).toHaveText(/ficha del producto|aprobado por el tendero/i);
    await chip.click();
    await expect(answer.locator('storefront-tino-source details')).toHaveAttribute('open', '');
  });

  test('answers about the shop from the shop’s own fact, and tells a guest how to ask about an order', async ({
    page,
  }) => {
    await door(page).click();

    await ask(page, '¿cuántos días tengo para devolver algo?');
    const policy = panel(page).locator('[data-kind="Shop"]').first();
    await expect(policy.getByText('Tienes 14 días desde la entrega.')).toBeVisible({ timeout: 30_000 });
    await expect(policy.locator('storefront-tino-source summary')).toHaveText(/normas de la tienda/i);

    // Nobody signed in and no order on the screen: the shop's notice, word for word, and no chip.
    await ask(page, '¿dónde está mi pedido?');
    const notice = panel(page).locator('[data-kind="Shop"]').nth(1);
    await expect(notice).toContainText(/inicia sesión o abre el pedido desde su correo/i, { timeout: 30_000 });
    await expect(notice.locator('storefront-tino-source')).toHaveCount(0);
  });

  test('shows the search’s results for the same words when Tino does not answer, and says so', async ({ page }) => {
    await door(page).click();

    // Nothing in the script for this: the scripted model writes no answer, as a model that fails.
    await ask(page, 'mochila');

    await expect(
      panel(page)
        .locator('lib-notice')
        .getByText(/tino no está respondiendo ahora/i),
    ).toBeVisible({
      timeout: 30_000,
    });
    await expect(panel(page).locator('[data-fallback] storefront-product-card').first()).toBeVisible();
    await expect(panel(page).locator('storefront-tino-source')).toHaveCount(0);

    // The rest of the shop is as it was: the header's search still answers.
    await page.getByRole('searchbox').first().fill('mochila');
    await page.getByRole('searchbox').first().press('Enter');
    await expect(page).toHaveURL(/q=mochila/);
    await expect(page.locator('main storefront-product-card').first()).toBeVisible();
  });

  test('is a sheet on a phone, closed with Esc, with the focus back on the door', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/es');

    await door(page).click();
    const sheet = page.getByRole('dialog', { name: /tino/i });
    await expect(sheet).toBeVisible();
    await expect(page.locator('#tino-question')).toBeFocused();

    await page.keyboard.press('Escape');

    await expect(sheet).toBeHidden();
    await expect(door(page)).toBeFocused();
    await expect(door(page)).toHaveAttribute('aria-pressed', 'false');
  });
});
