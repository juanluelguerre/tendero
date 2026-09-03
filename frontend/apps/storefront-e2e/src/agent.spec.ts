import { expect, test, type Page } from '@playwright/test';

/**
 * The shop opened to a browser agent (WebMCP, phase 6).
 *
 * **A browser is the only place this can be tested.** The tools are registered
 * against `navigator.modelContext`, executed by the browser's own agent, and
 * they call the same Angular services the interface calls — so there is no layer
 * underneath where any of that is observable. The unit tests cover the
 * registration mechanism and the degradation; what happens when a tool actually
 * runs against the running shop lives here.
 *
 * `navigator.modelContext` is Chrome-only and under incubation at the W3C Web
 * Machine Learning Community Group. Rather than depend on the runner's Chromium
 * having shipped it, these specs INSTALL a minimal one and capture what the shop
 * hands it. That tests our half — which is the half we own — and it keeps
 * working when the specification moves.
 */

interface ToolResult {
  readonly text: string;
  readonly isError: boolean;
}

/** A page with an agent in it, holding whatever the shop registered. */
async function withAgent(page: Page, path = '/es'): Promise<void> {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'modelContext', {
      value: {
        provideContext: (context: { tools: unknown[] }) => {
          (window as unknown as { __tools: unknown[] }).__tools = context.tools;
        },
      },
      configurable: true,
    });
  });

  await page.goto(path);
  await expect(page.getByRole('searchbox')).toBeVisible();
}

function call(page: Page, name: string, input: Record<string, unknown>): Promise<ToolResult> {
  return page.evaluate(
    async ([toolName, toolInput]) => {
      const tools = (window as unknown as { __tools: { name: string; execute: (i: unknown) => Promise<{ content: { text: string }[]; isError?: boolean }> }[] }).__tools;
      const tool = tools.find((candidate) => candidate.name === toolName as string);
      if (!tool) throw new Error(`No tool named ${toolName as string}`);

      const result = await tool.execute(toolInput);
      return { text: result.content[0].text, isError: Boolean(result.isError) };
    },
    [name, input] as const,
  );
}

test.describe('the agent surface', () => {
  test('offers exactly the tools the shop declares', async ({ page }) => {
    await withAgent(page);

    const names = await page.evaluate(() =>
      (window as unknown as { __tools: { name: string }[] }).__tools.map((tool) => tool.name),
    );

    // The list IS the contract with every agent that visits, and it lives in one
    // file so a specification change is one edit. A test on it is what makes
    // adding a fifth tool a deliberate act rather than a drift.
    expect(names).toEqual(['search_products', 'get_product', 'add_to_cart', 'get_cart']);
  });

  test('searches the shop and answers with a product code', async ({ page }) => {
    await withAgent(page);

    const result = await call(page, 'search_products', { query: 'zapatillas' });

    expect(result.isError).toBe(false);
    // The CODE, not the slug: a slug changes when a shopkeeper fixes a typo, and
    // handing an agent a key that moves is handing it a broken one (ADR 0026).
    expect(result.text).toMatch(/code [0-9A-HJKMNP-TV-Z]{10}/);
  });

  /**
   * The shop's refusals reach the agent AS refusals. A model reading "too short"
   * as prose may send the same query again; reading it as an error will not.
   */
  test('refuses what the shop refuses, and marks it as a refusal', async ({ page }) => {
    await withAgent(page);

    const short = await call(page, 'search_products', { query: 'z' });
    expect(short.isError).toBe(true);

    const missing = await call(page, 'get_product', { code: 'ZZZZZZZZZZ' });
    expect(missing.isError).toBe(true);
  });

  /**
   * The point of this surface. The agent adds to the SHOPPER'S cart — it is
   * running in their tab, with their cart token — so the badge in the header
   * changes without a second principal, a token of its own or a mandate.
   *
   * That is the whole difference between WebMCP and phase 11, and it is visible
   * in one assertion: nothing here authenticates.
   */
  test('adds to the shopper own cart, in the shopper own session', async ({ page }) => {
    await withAgent(page);

    const found = await call(page, 'search_products', { query: 'zapatillas' });
    const code = /code ([0-9A-HJKMNP-TV-Z]{10})/.exec(found.text)?.[1];
    expect(code).toBeTruthy();

    const product = await call(page, 'get_product', { code: code as string });
    const sku = /^\s{2}(\S+) —/m.exec(product.text)?.[1];
    expect(sku).toBeTruthy();

    const added = await call(page, 'add_to_cart', { sku: sku as string });
    expect(added.isError).toBe(false);

    // The header badge is the shopper's own, and it moved.
    await expect(page.locator('.cart__badge')).toBeVisible();
  });

  /**
   * The phase-3 promotion engine reaching an agent, suppressions and all.
   *
   * A discount that did not apply and a discount of zero look identical in a
   * total, which is why the engine returns a reason. An agent that can say "not
   * combinable with the summer sale" is more useful than one that can only read
   * the number — and it is the shop saying NO, which is this project's whole
   * argument, surviving the trip to a model.
   */
  test('reads the cart back with the discounts that did NOT apply', async ({ page }) => {
    await withAgent(page);

    const found = await call(page, 'search_products', { query: 'zapatillas' });
    const code = /code ([0-9A-HJKMNP-TV-Z]{10})/.exec(found.text)?.[1];

    const product = await call(page, 'get_product', { code: code as string });
    const sku = /^\s{2}(\S+) —/m.exec(product.text)?.[1];

    await call(page, 'add_to_cart', { sku: sku as string, quantity: 2 });

    const cart = await call(page, 'get_cart', {});

    expect(cart.isError).toBe(false);
    expect(cart.text).toContain(sku as string);
    expect(cart.text).toContain('not applied —');
  });

  /**
   * `P6-5`, in the browser. Without `navigator.modelContext` nothing registers,
   * nothing throws and the shop is unchanged — which is what "degrades
   * gracefully" is supposed to mean and rarely does.
   */
  test('is simply absent on a browser that cannot host an agent', async ({ page }) => {
    const failures: string[] = [];
    page.on('pageerror', (error) => failures.push(String(error)));

    await page.goto('/es');

    await expect(page.getByRole('searchbox')).toBeVisible();
    expect(await page.evaluate(() => 'modelContext' in navigator)).toBe(false);
    expect(failures).toEqual([]);
  });
});
