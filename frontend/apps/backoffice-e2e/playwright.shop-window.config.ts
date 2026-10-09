import { defineConfig, devices } from '@playwright/test';

/**
 * The shop's window, for the storefront's specs: its whole catalogue on sale.
 *
 * A configuration of its own and not a project of `playwright.config.ts`,
 * because a plain `playwright test` runs every project there is — and this
 * suite's own specs must not find a hundred and twenty products published by a
 * run that was only meant to check an order. It is asked for by name, by the
 * storefront's `global-setup.ts`, and by nothing else.
 *
 * Here and not in the storefront's suite because putting a catalogue on sale is
 * the shopkeeper's to do, and this is where a shopkeeper signs in.
 */
export default defineConfig({
  testDir: './src',
  testMatch: /shop-window\.setup\.ts/,
  workers: 1,
  // The sign-in is a redirect through the issuer, as in the suite's own configuration.
  timeout: 60_000,
  reporter: process.env['CI'] ? [['github']] : 'list',
  use: {
    baseURL: process.env['BACKOFFICE_URL'] ?? 'http://localhost:4201',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'shop-window', use: { ...devices['Desktop Chrome'] } }],
});
