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

  /**
   * Sixty seconds, and the reason is the sign-in flow rather than a slow suite.
   *
   * Signing in used to be one click on a picker. It is now a redirect: the app
   * initializer asks the API which issuer it trusts, downloads that issuer's
   * discovery document and its keys, and only then does the first route resolve
   * — and every one of those is a network round trip before anything renders.
   * On a cold CI runner that ran past the default thirty and took a stocktake
   * with it.
   *
   * The number is a budget for a browser doing real work, not a place to hide a
   * hang: an assertion that is genuinely wrong still fails, five seconds later.
   */
  timeout: 60_000,

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
