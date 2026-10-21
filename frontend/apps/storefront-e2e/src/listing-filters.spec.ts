import { expect, test, type Page } from '@playwright/test';

/**
 * The listing's filters in a browser (R-7.3, F-3 and F-4): what the unit tests
 * of the URL functions cannot prove — that a choice in the column becomes the
 * address, that the back button undoes one, that a fold survives a reload, and
 * that the phone's sheet applies its choices as one batch and gives the focus
 * back.
 *
 * Against `SHOES` (*Zapatillas deportivas*), the seed's category with two brands
 * and several colours. The options are read from the page rather than named, so
 * a changed catalogue changes the choices and not the test; a section the
 * running stack does not offer (no colours indexed yet) is skipped, not failed.
 */

const Category = '/es/c/SHOES';

async function desktop(page: Page): Promise<void> {
  await page.setViewportSize({ width: 1440, height: 900 });
}

/** The listing has answered: its count says how many, not "Buscando…". */
async function listed(page: Page): Promise<void> {
  await expect(page.locator('.toolbar__count[role="status"]')).toContainText(/\d/);
}

test('on a desktop, a category narrows by brand, colour and price, and the URL, the count and the chips follow', async ({
  page,
}) => {
  await desktop(page);
  await page.goto(Category);
  await listed(page);

  const column = page.locator('#listing-filters');
  const count = page.locator('.toolbar__count[role="status"]');
  const chips = page.locator('.chips .chip');
  const before = (await count.textContent())?.trim();

  // Brand: a checkbox, the count in its name.
  const brands = column.getByRole('group', { name: 'Marca' });
  await expect(brands).toBeVisible();
  const firstBrand = brands.locator('label.check').first();
  const brandName = ((await firstBrand.locator('.check__label').textContent()) ?? '').trim();
  await firstBrand.getByRole('checkbox').check();

  await expect(page).toHaveURL(/[?&]brand=/);
  await expect(chips).toHaveCount(1);
  await expect(chips.first()).toHaveText(brandName);
  await expect(count).not.toHaveText(before ?? '');

  // Colour: a swatch with its name beside it, pressed once chosen.
  const colours = column.getByRole('group', { name: 'Color' });
  const hasColours = (await colours.count()) > 0;
  if (hasColours) {
    const colour = colours.locator('button.swatch:not([disabled])').first();
    await colour.click();
    await expect(page).toHaveURL(/[?&]f=COLOR(%3A|:)/);
    await expect(colour).toHaveAttribute('aria-pressed', 'true');
  }

  // Price: a band fills the two boxes, and the URL holds the range, never the band.
  const price = column.getByRole('group', { name: 'Precio' });
  await price.locator('input[type="radio"]:not([disabled])').first().click();
  await expect(page).toHaveURL(/[?&]price=\d*(\.\d+)?-\d*(\.\d+)?/);
  // The lowest band is "Hasta …": it fills the upper box.
  await expect(price.getByRole('textbox', { name: 'Precio máximo' })).not.toHaveValue('');

  const applied = hasColours ? 3 : 2;
  await expect(chips).toHaveCount(applied);

  // A chip takes its one filter off.
  await page
    .locator('.chips')
    .getByRole('button', { name: `Quitar el filtro ${brandName}` })
    .click();
  await expect(page).not.toHaveURL(/[?&]brand=/);
  await expect(chips).toHaveCount(applied - 1);

  // The back button undoes one step: the brand comes back.
  await page.goBack();
  await expect(page).toHaveURL(/[?&]brand=/);
  await expect(chips).toHaveCount(applied);

  // *Quitar todo* clears every filter and leaves the category.
  await page.locator('.chips').getByRole('button', { name: 'Quitar todo' }).click();
  await expect(chips).toHaveCount(0);
  await expect(page).toHaveURL(/\/es\/c\/SHOES$/);
  await expect(count).toHaveText(before ?? '');
});

test('on a desktop, the folded column stays folded after a reload', async ({ page }) => {
  await desktop(page);
  await page.goto(Category);
  await listed(page);

  const column = page.locator('#listing-filters');
  await expect(column).toBeVisible();

  await page.getByRole('button', { name: 'Ocultar filtros' }).click();
  await expect(column).toBeHidden();
  const show = page.getByRole('button', { name: /^Mostrar filtros/ });
  await expect(show).toHaveAttribute('aria-expanded', 'false');

  await page.reload();
  await listed(page);
  await expect(column).toBeHidden();

  await show.click();
  await expect(column).toBeVisible();
});

test('on a phone, the sheet collects choices and applies them as one batch with Ver N productos', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(Category);
  await listed(page);
  const unfiltered = /\d+/.exec((await page.locator('.toolbar__count[role="status"]').textContent()) ?? '')?.[0];

  // No column on a phone: the filters are behind a button beside the order.
  await expect(page.locator('#listing-filters')).toBeHidden();
  const open = page.getByRole('button', { name: /^Filtros/ });
  await open.click();

  const sheet = page.getByRole('dialog', { name: 'Filtros' });
  await expect(sheet).toBeVisible();

  const firstBrand = sheet.getByRole('group', { name: 'Marca' }).locator('label.check').first();
  const brandName = ((await firstBrand.locator('.check__label').textContent()) ?? '').trim();
  await firstBrand.getByRole('checkbox').check();

  // Nothing applied yet: the page does not reload under a thumb.
  await expect(page).not.toHaveURL(/[?&]brand=/);

  // N is asked of the API once the taps settle; the one clay action says it.
  // Read N once it is the batch's: for a frame after the tap the button can
  // still carry the list's own total, and a brand in a two-brand category
  // always narrows it.
  const apply = sheet.locator('.sheet__apply');
  const shown = async () => /^\s*Ver (\d+) productos?\s*$/.exec((await apply.textContent()) ?? '')?.[1] ?? null;
  const batch = async () => {
    const count = await shown();
    return count !== null && count !== unfiltered ? count : null;
  };
  await expect.poll(batch).toMatch(/^\d+$/);
  const promised = Number(await batch());
  await apply.click();

  await expect(sheet).toBeHidden();
  await expect(page).toHaveURL(/[?&]brand=/);
  await expect(page.locator('.chips .chip')).toHaveText([brandName]);
  await expect(open).toHaveText(/Filtros \(1\)/);
  await expect(open).toBeFocused();
  await expect(page.locator('.toolbar__count[role="status"]')).toContainText(String(promised));
});

test('on a phone, closing the sheet with Esc drops the unapplied choices', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(Category);
  await listed(page);

  const open = page.getByRole('button', { name: /^Filtros/ });
  await open.click();
  const sheet = page.getByRole('dialog', { name: 'Filtros' });
  await sheet.getByRole('group', { name: 'Marca' }).getByRole('checkbox').first().check();

  await page.keyboard.press('Escape');

  await expect(sheet).toBeHidden();
  await expect(page).not.toHaveURL(/[?&]brand=/);
  await expect(open).toBeFocused();
});

/**
 * A modal sheet holds the page still (R-6's second walkthrough): the platform
 * traps the focus inside a modal dialog but not the scroll, and a thumb on the
 * dimmed products behind the sheet moved them.
 */
test('on a phone, the products behind the open sheet do not scroll', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(Category);
  await listed(page);
  await page.getByRole('button', { name: /^Filtros/ }).click();
  await expect(page.getByRole('dialog', { name: 'Filtros' })).toBeVisible();

  const before = await page.evaluate(() => window.scrollY);
  // Over the dimmed page, above the sheet.
  await page.mouse.move(195, 60);
  await page.mouse.wheel(0, 600);
  await page.waitForTimeout(300);

  expect(await page.evaluate(() => window.scrollY)).toBe(before);
});

/**
 * A full-width sheet reaches both edges of the window (the author's capture of
 * 2026-10-16, Tino's sheet): the root keeps the scrollbar's gutter reserved so a
 * centred dialog does not shift the page, and the viewport paints that gutter
 * over everything, a modal dialog included, leaving a strip of the page at the
 * sheet's right. Measured as it is seen: what is under the window's last pixels,
 * not the sheet's box, which was the full width all along.
 */
test('on a phone, the sheet reaches both edges of the window', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(Category);
  await listed(page);
  await page.getByRole('button', { name: /^Filtros/ }).click();
  const sheet = page.getByRole('dialog', { name: 'Filtros' });
  await expect(sheet).toBeVisible();

  const edges = await sheet.evaluate((dialog) => {
    const y = dialog.getBoundingClientRect().top + 40;
    const inside = (x: number) => dialog.contains(document.elementFromPoint(x, y));
    return [inside(1), inside(window.innerWidth - 2)];
  });
  expect(edges).toEqual([true, true]);
});

test('Ofertas in the departments row lists everything on offer', async ({ page }) => {
  await desktop(page);
  await page.goto('/es/cart');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  await page.getByRole('navigation', { name: 'Departamentos' }).getByRole('link', { name: 'Ofertas' }).click();

  await expect(page).toHaveURL(/\/es\?offer=1$/);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Ofertas');
  await listed(page);
  await expect(page.locator('.chips .chip')).toHaveText([/oferta/i]);
  await expect(page.locator('#listing-filters').getByRole('switch', { name: /oferta/i })).toBeChecked();
});

test('a filtered category names the unfiltered one as canonical, and a search keeps itself out of the index', async ({
  page,
}) => {
  await page.goto(`${Category}?brand=Vertex&sort=price-asc`);
  await listed(page);
  await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', /\/es\/c\/SHOES$/);
  await expect(page.locator('link[rel="alternate"][hreflang="en"]')).toHaveAttribute('href', /\/en\/c\/SHOES$/);
  await expect(page.locator('meta[name="robots"]')).toHaveCount(0);

  await page.goto('/es?q=mochila');
  await listed(page);
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', 'noindex,follow');
  await expect(page.locator('link[rel="canonical"]')).toHaveCount(0);

  // Leaving the search takes the noindex with it: a product it led to is indexable.
  await page.locator('.listing__grid a.card__link').first().click();
  await expect(page).toHaveURL(/\/es\/p\//);
  await expect(page.locator('link[rel="canonical"]')).toHaveCount(1);
  await expect(page.locator('meta[name="robots"]')).toHaveCount(0);
});
