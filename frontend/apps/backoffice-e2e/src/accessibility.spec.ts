import * as fs from 'node:fs';
import * as path from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { auditPage, compareToBaseline, findingsOf } from '@tendero/shared-a11y';
import { signIn } from './session';

/**
 * The shopkeeper's screens against R-1's accessibility findings (the redesign's
 * spec), at a phone, a tablet and a desktop width.
 */
const Screens = [
  'dashboard', 'review', 'catalogue', 'claims', 'attributes', 'search',
  'promotions', 'stock', 'orders', 'returns', 'agents', 'audit',
].map((screen) => [screen, `/${screen}`] as const);

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
  if (fixed.length) console.log(`Fixed since the baseline (remove them with UPDATE_A11Y_BASELINE=1):\n  ${fixed.join('\n  ')}`);
  expect(added, 'New accessibility findings — fix them, or say why in the baseline').toEqual([]);
}

test('the backoffice gets no less accessible than R-1 found it', async ({ page }) => {
  test.setTimeout(240_000);
  await signIn(page);
  await hold(Screens, page);
});
