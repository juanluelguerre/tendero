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
    await expect(page.locator('nav.tabs a[aria-current="page"]')).toHaveAttribute('href', '/dashboard/sales?period=month');
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

  test('on a phone: no full screen, no sideways page, the tabs scroll', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/dashboard');
    await expect(page.locator('nav.tabs')).toBeVisible();
    await expect(page.locator('.full-screen-enter')).toBeHidden();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
    const tabs = await page.locator('nav.tabs').evaluate((nav) => [nav.scrollWidth > nav.clientWidth, getComputedStyle(nav).overflowX]);
    expect(tabs).toEqual([true, 'auto']);
  });
});
