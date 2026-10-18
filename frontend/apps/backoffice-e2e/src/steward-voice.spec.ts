import { expect, test } from '@playwright/test';
import { signIn } from './session';

/**
 * Speaking to Steward (phase 14, the shared composer of 2026-10-17): the same box
 * as Tino's, so the same microphone — drawn while the box is empty, the words it
 * heard written in unsent, and the arrow in its place once there is something to
 * send. Chromium's fake microphone plays a tone and the stack's scripted engine
 * (`Tendero__SpeechScript`, CI sets it) answers any audio with the script's words.
 */
test.use({
  permissions: ['microphone'],
  launchOptions: { args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream'] },
});

test.describe('Speaking to Steward', () => {
  test.skip(!process.env['VOICE_SCRIPTED'] && !process.env['CI'], 'needs the stack started with Tendero__SpeechScript');

  test('a question spoken to Steward lands in the box, unsent, and the arrow takes the microphone’s place', async ({
    page,
  }) => {
    await signIn(page);
    await page.goto('/catalogue');
    await page
      .getByRole('banner')
      .getByRole('button', { name: /^steward$/i })
      .click();

    let turns = 0;
    page.on('request', (request) => request.url().includes('/api/steward/turns') && turns++);

    const panel = page.locator('backoffice-steward-panel');
    const microphone = panel.locator('[data-microphone]');
    await expect(panel.locator('[data-send]')).toHaveCount(0);
    await microphone.click();
    await expect(microphone).toHaveAttribute('aria-pressed', 'true');
    await page.waitForTimeout(1200);
    await microphone.click();

    await expect(page.locator('#steward-question')).toHaveValue('zapatillas para correr por montaña', {
      timeout: 15_000,
    });
    await expect(panel.locator('[data-microphone]')).toHaveCount(0);
    await expect(panel.locator('[data-send]')).toBeEnabled();
    expect(turns).toBe(0);
  });
});
