import { defineConfig, devices } from '@playwright/test';

/**
 * The shopper's half of the browser layer, and the same thinness rule applies.
 *
 * The commerce loop is covered against a real Postgres by `CheckoutLoopTests`;
 * that a refund is 72.55 € is not this file's job. What only a browser can prove
 * is the wiring: that a URL carries the language, that a variant picker refuses
 * a combination the shop does not sell, and that a renamed product's link still
 * lands somewhere.
 *
 * **It does not start the stack.** Aspire brings up Postgres, Elasticsearch, the
 * API, the workers and both apps with one command, and a config that tried to
 * reproduce that with `webServer` would be a second, worse AppHost. The same
 * decision `tools/SearchEval` already makes about Elasticsearch: take a URL,
 * document the prerequisite.
 */
export default defineConfig({
  testDir: './src',

  // One worker. The specs share one cart token per browser context and move
  // real stock, so parallel runs would fight over the same rows — a property of
  // the shop, not a limitation of the runner.
  workers: 1,
  fullyParallel: false,

  // Retries in CI only. A green retry locally hides a flake; a red one in CI
  // that passes on the second attempt is still a signal worth keeping in the
  // report rather than in the build's exit code.
  retries: process.env['CI'] ? 1 : 0,

  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : 'list',

  use: {
    baseURL: process.env['STOREFRONT_URL'] ?? 'http://localhost:4200',

    // A trace of the failing attempt is what makes an agent useful for
    // diagnosis: it can read what actually happened rather than guess from a
    // stack.
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
