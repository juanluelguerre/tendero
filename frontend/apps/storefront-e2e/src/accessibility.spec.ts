import * as fs from 'node:fs';
import * as path from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { auditPage, compareToBaseline, findingsOf } from '@tendero/shared-a11y';

/**
 * The shopper's pages against R-1's accessibility findings (the redesign's spec),
 * at a phone, a tablet and a desktop width. A category and a product are the
 * first the home page links to, so the suite runs on whatever the stack holds.
 */

const Widths = [360, 768, 1440] as const;

/**
 * Holds the pages to what R-1 found and no worse: a finding the committed
 * baseline does not know fails the run, and one it knows that is gone is reported,
 * so the baseline shrinks as R-4 fixes things. `UPDATE_A11Y_BASELINE=1` rewrites
 * it. Contrast is WCAG's own formula (decided 2026-09-27: no axe-core, which is
 * MPL-2.0); keyboard paths stay a person's.
 */
async function hold(pages: readonly (readonly [string, string])[], page: Page): Promise<void> {
  const found: string[] = [];
  for (const [name, url] of pages) {
    for (const width of Widths) {
      await page.setViewportSize({ width, height: width < 800 ? 844 : 1000 });
      await page.goto(url);
      await page.waitForLoadState('networkidle');
      found.push(...findingsOf(name, width, await auditPage(page)));
    }
  }

  const file = path.join(__dirname, 'accessibility-baseline.json');
  if (process.env['UPDATE_A11Y_BASELINE']) {
    fs.writeFileSync(file, JSON.stringify([...new Set(found)].sort(), null, 2) + '\n');
    return;
  }

  const { added, fixed } = compareToBaseline([...new Set(found)].sort(), JSON.parse(fs.readFileSync(file, 'utf-8')));
  if (fixed.length)
    console.log(`Fixed since the baseline (remove them with UPDATE_A11Y_BASELINE=1):\n  ${fixed.join('\n  ')}`);
  expect(added, 'New accessibility findings — fix them, or say why in the baseline').toEqual([]);
}

test('the storefront gets no less accessible than R-1 found it', async ({ page }) => {
  test.setTimeout(240_000);
  await page.goto('/es');
  await page.waitForLoadState('networkidle');
  // The campaign's listing, when a campaign leads (R-8): its heading and its chip.
  const cta = page.locator('storefront-campaign-card a.campaign__cta');
  const campaign = (await cta.count())
    ? new URL((await cta.first().getAttribute('href')) ?? '/es', 'http://shop').searchParams.get('campaign')
    : null;
  const category = await page.locator('a[href*="/c/"]').first().getAttribute('href');
  // The rows draw their cards as they come near the screen, and a campaign with
  // a picture of its own shows none above the fold: scrolled to, as a person would.
  const productLink = page.locator('a[href*="/p/"]').first();
  for (let step = 0; step < 20 && !(await productLink.count()); step++) {
    await page.mouse.wheel(0, 600);
    await page.waitForTimeout(200);
  }
  const product = await productLink.getAttribute('href');

  // A product viewed and a search made, so the home is audited with its trail
  // row and *Tu historial* with entries in it (R-8) — the states a returning
  // shopper sees, not only a first visit's.
  await page.goto(product ?? '/es');
  await page.goto('/es?q=mochila');
  await page.waitForLoadState('networkidle');

  await hold(
    [
      ['home', '/es'],
      ['search', '/es?q=mochila'],
      ['category', category ?? '/es'],
      ['product', product ?? '/es'],
      ['cart', '/es/cart'],
      ['account', '/es/account'],
      ['agents', '/es/agents'],
      ['campaign', `/es?campaign=${campaign ?? ''}`],
    ],
    page,
  );
});
