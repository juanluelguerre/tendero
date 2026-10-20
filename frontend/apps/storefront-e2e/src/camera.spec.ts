import { expect, type Page, test } from '@playwright/test';

/**
 * The camera on a phone (the camera search, CS-6; the mock-up, screen 13): the
 * page's own viewfinder on Chromium's fake camera, and the phone's camera app
 * when the camera cannot be shown. A file of its own because the fake camera is
 * a launch option, and Playwright launches a browser once per worker.
 */
test.use({
  viewport: { width: 390, height: 844 },
  hasTouch: true,
  permissions: ['camera'],
  launchOptions: { args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream'] },
});

test.describe('the camera on a phone', () => {
  async function openPhone(page: Page) {
    await page.route('**/api/search/by-image/status', (route) =>
      route.fulfill({ json: { available: true, objects: true } }),
    );
    await page.goto('/es');
    const refuse = page.getByRole('button', { name: /no permitir|solo las necesarias|rechazar/i });
    if (await refuse.isVisible().catch(() => false)) await refuse.click();
    await page.locator('[data-visual-search-door]:visible').click();
  }

  test('the viewfinder freezes a frame, and that frame is the picture', async ({ page }) => {
    await openPhone(page);
    await page.locator('[data-visual-search-camera]').click();

    const shutter = page.locator('[data-viewfinder-shutter]');
    await expect(shutter).toBeVisible();
    await expect
      .poll(() => page.locator('storefront-viewfinder video').evaluate((v: HTMLVideoElement) => v.videoWidth))
      .toBeGreaterThan(0);
    await shutter.click();
    await page.locator('[data-viewfinder-use]').click();

    await expect(page.locator('storefront-picture-sheet dialog')).toBeVisible();
    await expect(page.locator('storefront-viewfinder')).toHaveCount(0);
  });

  test('refused the camera, it opens the phone’s own camera app instead', async ({ page }) => {
    await page.addInitScript(() => {
      navigator.mediaDevices.getUserMedia = () => Promise.reject(new DOMException('denied', 'NotAllowedError'));
    });
    await openPhone(page);

    const chooser = page.waitForEvent('filechooser');
    await page.locator('[data-visual-search-camera]').click();
    expect((await chooser).element()).toBeTruthy();
    await expect(page.locator('storefront-viewfinder')).toHaveCount(0);
  });
});
