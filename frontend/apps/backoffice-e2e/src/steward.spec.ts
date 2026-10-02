import { expect, test } from '@playwright/test';
import { signIn } from './session';

/**
 * Steward, in a browser (phase 12; spec, *Evaluation and tests*): a question, the
 * card its proposal arrives as, the review it goes through, and the audit row
 * that says the change was proposed by Steward and approved by a person.
 *
 * The stack answers from a script (`Tendero__StewardScript`, CI sets it): no model
 * fits a runner, and what this proves is the wiring — the panel, the agent, the
 * contexts' tools, Assist, the approval running the command as the approver, the
 * audit — not a model's judgement, which the copilot suite measures.
 */
test.describe('Steward', () => {
  test.skip(
    !process.env['STEWARD_SCRIPTED'] && !process.env['CI'],
    'needs the stack started with Tendero__StewardScript',
  );

  test('proposes, is reviewed, applies as the person, and the audit names the proposal', async ({ page }) => {
    await signIn(page);
    await page.goto('/catalogue');

    await page
      .getByRole('banner')
      .getByRole('button', { name: /^steward$/i })
      .click();
    const composer = page.locator('#steward-question');
    await expect(composer).toBeFocused();
    await composer.fill('Give the Pulse Runner its English name.');
    await composer.press('Enter');

    // The card the proposal arrives as, and the review it goes through.
    const review = page.getByRole('button', { name: /^review the proposal$/i });
    await expect(review).toBeVisible({ timeout: 30_000 });
    await review.click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByText('Pulse Runner cushioned running shoes').first()).toBeVisible();
    await dialog.getByRole('button', { name: /^approve 1$/i }).click();
    await expect(dialog.getByText(/^applied$/i)).toBeVisible();
    await dialog.getByRole('button', { name: /^close$/i }).click();

    // The audit says who approved it and that Steward proposed it.
    // It opens on the refusals; an approval is Allowed.
    await page.goto('/audit');
    await page.getByRole('button', { name: /^allowed/i }).click();
    await expect(page.getByRole('link', { name: /proposed by steward/i }).first()).toBeVisible();
  });
});
