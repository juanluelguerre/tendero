import { defineConfig, devices } from '@playwright/test';

/**
 * The browser half of the test pyramid, and deliberately the thinnest layer.
 *
 * Everything below it already runs: 395 tests, five of which drive the whole
 * commerce loop against a real Postgres and the real outbox. What only a browser
 * can prove is the wiring between Angular and the API — the route guard, the
 * token interceptor, and a table that derives its buttons from a status the
 * server sent. That is what lives here, and nothing else.
 *
 * **It does not start the stack.** Aspire brings up Postgres, Elasticsearch, the
 * API, the workers and both apps with one command, and a config that tried to
 * reproduce that with `webServer` would be a second, worse AppHost. The same
 * decision `tools/SearchEval` already makes about Elasticsearch: take a URL,
 * document the prerequisite.
 */
export default defineConfig({
  testDir: './src',

  // One worker. The specs move real orders through a real state machine against
  // one database, and parallel runs would fight over the same rows — which is a
  // property of the shop, not a limitation of the runner.
  workers: 1,
  fullyParallel: false,

  // Retries in CI only. A green retry locally hides a flake; a red one in CI
  // that passes on the second attempt is still a signal worth keeping in the
  // report rather than in the build's exit code.
  retries: process.env['CI'] ? 1 : 0,

  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : 'list',

  use: {
    baseURL: process.env['BACKOFFICE_URL'] ?? 'http://localhost:4201',

    // A trace of the failing attempt is what makes an agent useful for
    // diagnosis: it can read what actually happened rather than guess from a
    // stack.
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
