import { expect, test } from '@playwright/test';
import { signIn } from './session';

/**
 * What only a browser can prove about R-9's dashboard: widgets choosing their
 * content from their own width (container queries, which jsdom does not
 * evaluate), the tabs' URLs, full screen, and the phone.
 */
test.describe('the dashboard', () => {
  test.beforeEach(async ({ page }) => signIn(page));

  test('keeps its tab and its period in the URL', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto('/dashboard/sales?period=month');
    await expect(page.locator('nav.tabs a[aria-current="page"]')).toHaveAttribute(
      'href',
      '/dashboard/sales?period=month',
    );
    await page.locator('nav.tabs a[href^="/dashboard/operations"]').click();
    await expect(page).toHaveURL(/\/dashboard\/operations\?period=month$/);
    await expect(page.locator('.period[aria-pressed="true"]')).toHaveAttribute('data-period', 'Month');
  });

  test('a widget shows its figure at S, its chart at M, its chart and table at L', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto('/dashboard');
    // By channel is S on the Overview: its figure, not its chart.
    const byChannel = page.locator('[data-widget="byChannel"]');
    await expect(byChannel.locator('.only-s')).toBeVisible();
    await expect(byChannel.locator('.at-m')).toBeHidden();
    // Sales by day is M there: the chart, its table behind a toggle.
    await expect(page.locator('[data-widget="salesByDay"] .figures-toggle')).toBeVisible();

    await page.goto('/dashboard/sales');
    // And L on Sales: the chart and its table, shown without asking.
    const salesByDay = page.locator('[data-widget="salesByDay"]');
    await expect(salesByDay.locator('table.figures')).toBeVisible();
    await expect(salesByDay.locator('.figures-toggle')).toBeHidden();
  });

  /**
   * Esc is the browser's own, not a key the page receives: a key Playwright
   * presses is a DOM event and leaves full screen standing. The way out tested
   * here is the page's button; that the page follows the browser leaving (Esc
   * included) is `full-screen.spec.ts`'s, on the fullscreenchange event.
   */
  test('goes full screen on a monitor and comes back', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto('/dashboard/operations');
    await page.locator('.full-screen-enter').click();
    await expect.poll(() => page.evaluate(() => document.fullscreenElement?.className ?? null)).toContain('dashboard');
    await expect(page.locator('.wall-head')).toBeVisible();
    await page.locator('.full-screen-leave').click();
    await expect.poll(() => page.evaluate(() => document.fullscreenElement)).toBeNull();
    await expect(page.locator('nav.tabs')).toBeVisible();
  });

  /** The tab fills the monitor (spec): the rows share its height, so no tab's wall scrolls — the fullest ones included. */
  test('fits every tab on the wall without scrolling', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    for (const tab of [
      '/dashboard',
      '/dashboard/sales',
      '/dashboard/operations',
      '/dashboard/growth',
      '/dashboard/agents',
    ]) {
      await page.goto(tab);
      // eslint-disable-next-line playwright/no-networkidle -- the wall is measured once every widget has painted its data; no single element says that
      await page.waitForLoadState('networkidle');
      await page.locator('.full-screen-enter').click();
      await expect(page.locator('.wall-head')).toBeVisible();
      await expect
        .poll(() => page.locator('section.dashboard').evaluate((wall) => wall.scrollHeight - wall.clientHeight), {
          message: tab,
        })
        .toBeLessThanOrEqual(0);
      // And no widget spills out of its row onto the one below.
      const spilled = await page
        .locator('.cell')
        .evaluateAll((cells) =>
          cells
            .filter((cell) => cell.scrollHeight > cell.clientHeight + 1)
            .map((cell) => (cell as HTMLElement).dataset['widget']),
        );
      expect(spilled, tab).toEqual([]);
      await page.locator('.full-screen-leave').click();
      await expect(page.locator('nav.tabs')).toBeVisible();
    }
  });

  test('on a phone: no full screen, no sideways page, the tabs scroll', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/dashboard');
    await expect(page.locator('nav.tabs')).toBeVisible();
    await expect(page.locator('.full-screen-enter')).toBeHidden();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
    const tabs = await page
      .locator('nav.tabs')
      .evaluate((nav) => [nav.scrollWidth > nav.clientWidth, getComputedStyle(nav).overflowX]);
    expect(tabs).toEqual([true, 'auto']);
  });

  /**
   * The author's screenshot of 2026-10-16: Sales by day drawn under Waiting for
   * you and Health. ECharts writes its SVG at the width it was drawn at, and a grid
   * track is never narrower than its content, so a cell that narrowed while the
   * window stayed the same — Steward's panel docked beside the page — kept the old
   * width, the chart's resize observer never saw it shrink, and the drawing spilled
   * 193 px over its neighbours. A wider or narrower window did not show it.
   */
  test('a chart narrows with its cell when Steward docks beside the page', async ({ page }) => {
    const spills = () =>
      page
        .locator('[data-widget]')
        .evaluateAll((cells) =>
          cells.flatMap((cell) =>
            [...cell.querySelectorAll('figure [aria-hidden="true"] svg')]
              .filter((svg) => svg.getBoundingClientRect().right > cell.getBoundingClientRect().right + 1)
              .map(() => cell.getAttribute('data-widget')),
          ),
        );
    await page.setViewportSize({ width: 1780, height: 1100 });
    await page.goto('/dashboard');
    await expect(page.locator('[data-widget="salesByDay"] figure [aria-hidden="true"] svg')).toBeVisible();

    await page
      .getByRole('button', { name: /steward/i })
      .first()
      .click();
    await expect.poll(spills).toEqual([]);
  });
});
