import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { backofficeUrl, signInToTheBackoffice, signInToTheShop } from './journeys';

/**
 * The home that sells (R-8, docs/specs/storefront-home.md), in a browser: the
 * campaign the shop leads with, *Sigue donde lo dejaste* filling and emptying,
 * a campaign's listing, a category's banner, and a guest's trail following them
 * into their account. The rules behind each are unit-tested; what only a
 * browser shows is that the pieces meet.
 *
 * It reads the shop's own answers rather than assuming the seed: the campaign
 * that leads depends on today's date against the seed's windows.
 */

interface Campaign {
  code: string;
  name: string;
  scope: string;
  phase: string;
}

async function campaignsOf(request: APIRequestContext): Promise<Campaign[]> {
  const response = await request.get('/api/pricing/campaigns?culture=es');
  expect(response.ok()).toBe(true);
  return ((await response.json()) as { items: Campaign[] }).items;
}

/** The first N products a search lists, as their pages' addresses. */
async function productsFor(page: Page, query: string, count: number): Promise<{ href: string; name: string }[]> {
  await page.goto(`/es?q=${encodeURIComponent(query)}`);
  const links = page.locator('.listing__grid a.card__link');
  await expect(links.first()).toBeVisible();
  const found = [];
  for (let i = 0; i < count; i++) {
    found.push(
      await links
        .nth(i)
        .evaluate((anchor) => ({ href: anchor.getAttribute('href') ?? '', name: (anchor.textContent ?? '').trim() })),
    );
  }
  return found;
}

async function view(page: Page, product: { href: string; name: string }): Promise<void> {
  await page.goto(product.href);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(product.name);
}

const trailRow = (page: Page) => page.locator('storefront-trail-row');

test.describe('the home page', () => {
  test('leads with the campaign the shop puts first, or with none', async ({ page, request }) => {
    const leading = (await campaignsOf(request)).find((campaign) => campaign.scope === 'Shop');

    await page.goto('/es');
    await expect(page.getByRole('heading', { level: 2, name: 'Compra con tu agente' })).toBeVisible();

    if (!leading) {
      await expect(page.locator('storefront-campaign-card')).toHaveCount(0);
      return;
    }

    const card = page.locator('storefront-campaign-card');
    await expect(card.getByRole('heading', { level: 2 })).toHaveText(leading.name);
    await expect(card.locator('a.campaign__cta')).toHaveAttribute('href', new RegExp(`campaign=${leading.code}`));
  });

  test('lists only what the campaign reaches, headed with its name', async ({ page, request }) => {
    const leading = (await campaignsOf(request)).find((campaign) => campaign.scope === 'Shop');
    test.skip(!leading, 'No shop-wide campaign is near the date the stack runs on.');
    if (!leading) return;

    await page.goto('/es');
    await page.locator('storefront-campaign-card a.campaign__cta').click();

    await expect(page).toHaveURL(new RegExp(`/es\\?campaign=${leading.code}$`));
    await expect(page.getByRole('heading', { level: 1 })).toContainText(leading.name);

    const reached = (await (
      await request.get(`/api/search?campaign=${leading.code}&culture=es&pageSize=50`)
    ).json()) as {
      total: number;
      hits: { code: string }[];
    };

    if (leading.phase === 'Upcoming') {
      // Before the window opens its promotions reach nothing, and the page says why.
      await expect(page.getByText(`${leading.name} empieza el`)).toBeVisible();
      return;
    }

    const codes = await page
      .locator('.listing__grid a.card__link')
      .evaluateAll((links) => links.map((link) => (link.getAttribute('href') ?? '').split('/').pop()));
    expect(codes.length).toBeGreaterThan(0);
    expect(codes.every((code) => reached.hits.some((hit) => hit.code === code))).toBe(true);
    // The whole list is the campaign's, and a chip names it so it can be taken off.
    await expect(page.getByRole('button', { name: new RegExp(`Campaña: ${leading.name}`) })).toBeVisible();
  });

  test('remembers what a guest viewed and searched, and forgets it on request', async ({ page }) => {
    await page.goto('/es');
    await expect(trailRow(page)).toHaveCount(0);

    const [first, second] = await productsFor(page, 'mochila', 2);
    await view(page, first);
    await view(page, second);

    await page.goto('/es');
    const row = trailRow(page);
    await expect(row.getByRole('heading', { level: 2, name: 'Sigue donde lo dejaste' })).toBeVisible();
    await row.scrollIntoViewIfNeeded();
    const names = row.locator('.shelf__row a.card__link');
    await expect(names).toHaveText([second.name, first.name]);
    await expect(row.getByRole('link', { name: 'mochila', exact: true })).toBeVisible();

    await row.getByRole('button', { name: `Quitar ${second.name} de tu historial` }).click();
    await expect(names).toHaveText([first.name]);

    await row.getByRole('button', { name: 'Borrar historial' }).click();
    await expect(trailRow(page)).toHaveCount(0);
  });

  test('moves a guest trail into the account at sign-in', async ({ page }) => {
    const [product] = await productsFor(page, 'cafetera', 1);
    await view(page, product);

    await signInToTheShop(page);

    const history = page.locator('storefront-trail-history');
    await expect(history.getByRole('heading', { name: 'Tu historial' })).toBeVisible();
    await expect(history.locator('a.card__link', { hasText: product.name })).toBeVisible();
    // One trail, not two: the browser's copy went with the merge.
    expect(
      await page.evaluate(() => JSON.parse(localStorage.getItem('tendero.trail') ?? '{"views":[]}').views),
    ).toEqual([]);

    // Leave the account as it was found.
    await history.getByRole('button', { name: 'Borrar todo' }).click();
    await expect(history.getByText('Todavía no hay nada en tu historial.')).toBeVisible();
  });

  /**
   * A category's campaign is the banner of its pages and never the home's. The
   * seed has none, so the test makes one — through the backoffice's own API, as
   * the shopkeeper — and ends it afterwards.
   */
  test('shows a category campaign as the banner of that category', async ({ page, browser }) => {
    const code = `E2E_${Date.now().toString(36).toUpperCase()}`;
    const shopkeeper = await browser.newPage();
    await signInToTheBackoffice(shopkeeper);
    const token = await shopkeeper.evaluate(() => sessionStorage.getItem('access_token'));
    test.skip(!token, 'The backoffice keeps its token somewhere this test cannot read.');
    const headers = { Authorization: `Bearer ${token}` };

    const starts = new Date(Date.now() + 5 * 60_000);
    const created = await shopkeeper.request.post(`${backofficeUrl}/api/pricing/campaign-calendar`, {
      headers,
      data: {
        code,
        details: {
          name: { es: 'Semana del hogar', en: 'Home week' },
          line: { es: 'Lo de casa, a precio de barrio', en: 'Home things at corner-shop prices' },
          kind: 'Event',
          categoryCode: 'HOME',
          startsAt: starts.toISOString(),
          endsAt: new Date(starts.getTime() + 86_400_000).toISOString(),
          priority: 1,
          promotionCodes: ['KITCHEN5'],
        },
      },
    });
    expect(created.ok(), await created.text()).toBe(true);
    expect(
      (
        await shopkeeper.request.post(`${backofficeUrl}/api/pricing/campaign-calendar/${code}/schedule`, { headers })
      ).ok(),
    ).toBe(true);

    try {
      await page.goto('/es/c/KITCHEN');
      const banner = page.locator('storefront-campaign-card');
      await expect(banner.getByRole('heading', { name: 'Semana del hogar' })).toBeVisible();
      await expect(banner).toContainText('Empieza en');

      // Never the home's: one headline for the shop, the others where they apply.
      await page.goto('/es');
      await expect(page.getByRole('heading', { name: 'Semana del hogar' })).toHaveCount(0);
    } finally {
      await shopkeeper.request.post(`${backofficeUrl}/api/pricing/campaign-calendar/${code}/end`, { headers });
      await shopkeeper.close();
    }
  });
});
