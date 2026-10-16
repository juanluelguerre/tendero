import { expect, test, type Page } from '@playwright/test';

/**
 * Speaking to the shop, in a browser (phase 14; the voice spec, V-2 to V-4).
 * Chromium's fake microphone plays a tone, the page records it, encodes the WAV
 * the shop listens to, and the stack's scripted engine (`Tendero__SpeechScript`,
 * CI sets it) answers any audio with the script's words. What this proves is the
 * wiring — the microphone drawn, the recording, the WAV, the words landing in the
 * box unsent — not how well a model hears, which the measurement does (V-5).
 */
// Chromium's own fake microphone, playing a tone, and its permission granted: one
// browser for the file, as Playwright launches it once per worker.
test.use({
  permissions: ['microphone'],
  launchOptions: { args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream'] },
});

test.describe('Voice', () => {
  test.skip(!process.env['VOICE_SCRIPTED'] && !process.env['CI'], 'needs the stack started with Tendero__SpeechScript');

  const words = 'zapatillas para correr por montaña';

  async function refuseAnalytics(page: Page) {
    const refuse = page.getByRole('button', { name: /no permitir|solo las necesarias|rechazar/i });
    if (await refuse.isVisible().catch(() => false)) await refuse.click();
  }

  test('a question spoken to Tino lands in the box, and nothing is asked until it is sent', async ({ page }) => {
    await page.goto('/es');
    await refuseAnalytics(page);
    await page.locator('[data-tino-door]').click();

    let turns = 0;
    page.on('request', (request) => request.url().includes('/api/assistant/turns') && turns++);

    const microphone = page.locator('storefront-tino-panel [data-microphone]');
    await microphone.click();
    await expect(microphone).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#tino-question')).toHaveAttribute('placeholder', /escuchando/i);
    await page.waitForTimeout(1200);
    await microphone.click();

    await expect(page.locator('#tino-question')).toHaveValue(words, { timeout: 15_000 });
    await expect(page.getByText(/revísalo y envíalo/i)).toBeVisible();
    expect(turns).toBe(0);
  });

  test.describe('on a phone', () => {
    test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true });

    test('a question spoken into the search box lands there, and nothing is searched until it is sent', async ({
      page,
    }) => {
      await page.goto('/es');
      await refuseAnalytics(page);

      const box = page.locator('#find-full');
      const microphone = page.locator('form:has(#find-full) [data-microphone]');
      await microphone.click();
      await expect(box).toHaveAttribute('placeholder', /escuchando/i);
      await page.waitForTimeout(1200);
      await microphone.click();

      await expect(box).toHaveValue(words, { timeout: 15_000 });
      await expect(page).toHaveURL(/\/es$/);
    });

    test('the header box on a desktop has no microphone; the phone row has one', async ({ page }) => {
      await page.goto('/es');
      await expect(page.locator('form:has(#find-row) [data-microphone]')).toHaveCount(0);
      await expect(page.locator('form:has(#find-full) [data-microphone]')).toHaveCount(1);
    });
  });
});

/**
 * The degraded path (invariant 8), on the stack as anyone starts it: no engine,
 * so no microphone anywhere, and every box as it always was. It needs no script,
 * and it runs where the scripted stack does not.
 */
test.describe('Voice, with nothing to listen', () => {
  test.skip(
    !!process.env['VOICE_SCRIPTED'] || !!process.env['CI'],
    'the stack is listening: the other describe covers it',
  );

  test('no microphone in Tino or in the search box', async ({ page }) => {
    await page.goto('/es');
    await expect(page.locator('[data-microphone]')).toHaveCount(0);
  });
});
