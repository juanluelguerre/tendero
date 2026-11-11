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
  ...[
    'dashboard',
    'review',
    'catalogue',
    'claims',
    'attributes',
    'search',
    'promotions',
    'campaigns',
    'stock',
    'orders',
    'returns',
    'agents',
    'audit',
  ].map((screen) => [screen, `/${screen}`] as const),
  // The dashboard's four tabs (R-9), each a URL of its own; the Overview is `dashboard` above.
  ...['sales', 'operations', 'growth', 'agents'].map((tab) => [`dashboard-${tab}`, `/dashboard/${tab}`] as const),
  // A pane is where the fields are: a new campaign's form, and a seeded one read-only.
  ['campaigns-new', '/campaigns?campaign=new'],
  ['campaigns-scheduled', '/campaigns?status=all&campaign=TENDERO_DAYS'],
] as const;

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
      // eslint-disable-next-line playwright/no-networkidle -- the audit reads the page once everything it fetches has painted; no single element says that
      await page.waitForLoadState('networkidle');
      found.push(...findingsOf(name, width, await auditPage(page)));
    }
  }

  // One baseline, several tests: each holds and rewrites only the pages it audited.
  const file = path.join(__dirname, 'accessibility-baseline.json');
  const audited = new Set(pages.map(([name]) => name));
  const mine = (finding: string) => audited.has(finding.slice(0, finding.indexOf('@')));
  const baseline: string[] = JSON.parse(fs.readFileSync(file, 'utf-8'));
  if (process.env['UPDATE_A11Y_BASELINE']) {
    const kept = baseline.filter((finding) => !mine(finding));
    fs.writeFileSync(file, JSON.stringify([...new Set([...kept, ...found])].sort(), null, 2) + '\n');
    return;
  }

  const { added, fixed } = compareToBaseline([...new Set(found)].sort(), baseline.filter(mine));
  if (fixed.length)
    console.log(`Fixed since the baseline (remove them with UPDATE_A11Y_BASELINE=1):\n  ${fixed.join('\n  ')}`);
  expect(added, 'New accessibility findings — fix them, or say why in the baseline').toEqual([]);
}

test('the backoffice gets no less accessible than R-1 found it', async ({ page }) => {
  test.setTimeout(240_000);
  await signIn(page);
  await hold(Screens, page);
});

/**
 * The dashboard in the dark theme too (spec, accessibility: "every tab, in both
 * themes"): a wall is often dark, and its chart colours are the ones most likely
 * to fail there.
 */
test('the dashboard stays as accessible in the dark theme', async ({ page }) => {
  test.setTimeout(240_000);
  await page.addInitScript(() => localStorage.setItem('tendero.theme', 'dark'));
  await signIn(page);
  await hold(
    Screens.filter(([name]) => name.startsWith('dashboard')).map(([name, url]) => [`${name}-dark`, url] as const),
    page,
  );
});
