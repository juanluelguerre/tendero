import { expect, test, type Page } from '@playwright/test';

/**
 * The shop opened to a browser agent (WebMCP, phase 6).
 *
 * **A browser is the only place this can be tested.** The tools are registered
 * against `document.modelContext`, executed by the browser's own agent, and
 * they call the same Angular services the interface calls — so there is no layer
 * underneath where any of that is observable. The unit tests cover the
 * registration mechanism and the degradation; what happens when a tool actually
 * runs against the running shop lives here.
 *
 * `document.modelContext` is Chrome-only and under incubation at the W3C Web
 * Machine Learning Community Group. Rather than depend on the runner's Chromium
 * having shipped it, these specs INSTALL a minimal one and capture what the shop
 * hands it. That tests our half — which is the half we own.
 *
 * **The fake is the draft's shape and nothing else** (9 October 2026:
 * `registerTool(tool, options)`, a call a tool, refusing a name twice the way
 * the draft says). It first had `navigator.modelContext.provideContext`, and
 * went on passing after the specification dropped both — a fake that accepts
 * what no browser does proves only that we agree with ourselves.
 */

interface ToolResult {
  readonly text: string;
  readonly isError: boolean;
}

/** A page with an agent in it, holding whatever the shop registered. */
async function withAgent(page: Page, path = '/es'): Promise<void> {
  await page.addInitScript(() => {
    const tools: { name: string }[] = [];
    (window as unknown as { __tools: unknown[] }).__tools = tools;

    Object.defineProperty(document, 'modelContext', {
      value: {
        registerTool: (tool: { name: string }, options?: { signal?: AbortSignal }) => {
          if (tools.some((known) => known.name === tool.name))
            return Promise.reject(new DOMException(`${tool.name} is registered`, 'InvalidStateError'));

          tools.push(tool);
          options?.signal?.addEventListener('abort', () => tools.splice(tools.indexOf(tool), 1));
          return Promise.resolve();
        },
      },
      configurable: true,
    });
  });

  await page.goto(path);
  await expect(page.getByRole('searchbox').first()).toBeVisible();
}

function call(page: Page, name: string, input: Record<string, unknown>): Promise<ToolResult> {
  return page.evaluate(
    async ([toolName, toolInput]) => {
      const tools = (
        window as unknown as {
          __tools: {
            name: string;
            execute: (
              i: unknown,
              options: { signal: AbortSignal },
            ) => Promise<{ content: { text: string }[]; isError?: boolean }>;
          }[];
        }
      ).__tools;
      const tool = tools.find((candidate) => candidate.name === (toolName as string));
      if (!tool) throw new Error(`No tool named ${toolName as string}`);

      const result = await tool.execute(toolInput, { signal: new AbortController().signal });
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
    // adding a tool a deliberate act rather than a drift. The two filter tools
    // were added on purpose with the facets (R-7.3, F-5), and list_campaigns with
    // the home that sells (R-8, H-5).
    expect(names).toEqual([
      'search_products',
      'list_filters',
      'apply_filter',
      'get_product',
      'add_to_cart',
      'get_cart',
      'list_campaigns',
    ]);
  });

  /**
   * What an agent may do unasked. Five of the seven only read; narrowing the
   * list moves the page the shopper is looking at and adding to the basket
   * changes it, so neither says it only reads. Nothing here is *consequential*
   * in the draft's sense — significant, real-world or not reversible: a basket
   * is emptied with a click, and no tool pays.
   */
  test('says which tools only read', async ({ page }) => {
    await withAgent(page);

    const said = await page.evaluate(() =>
      (
        window as unknown as {
          __tools: { name: string; annotations?: { readOnlyHint?: boolean; consequentialHint?: boolean } }[];
        }
      ).__tools.map((tool) => [
        tool.name,
        tool.annotations?.readOnlyHint ?? false,
        tool.annotations?.consequentialHint ?? false,
      ]),
    );

    expect(said).toEqual([
      ['search_products', true, false],
      ['list_filters', true, false],
      ['apply_filter', false, false],
      ['get_product', true, false],
      ['add_to_cart', false, false],
      ['get_cart', true, false],
      ['list_campaigns', true, false],
    ]);
  });

  /**
   * "Are there deals this week?" answered from the list the home draws (R-8, H-5),
   * not scraped from a card: the same service, the same order.
   */
  test('lists the campaigns the shop is running or about to run', async ({ page, request }) => {
    await withAgent(page);

    const result = await call(page, 'list_campaigns', {});
    const campaigns = (
      (await (await request.get('/api/pricing/campaigns?culture=es')).json()) as { items: { code: string }[] }
    ).items;

    expect(result.isError).toBe(false);
    if (campaigns.length === 0) {
      expect(result.text).toContain('No campaign is running');
    } else {
      expect(result.text).toContain(`code ${campaigns[0].code}`);
      expect(result.text).toContain(`/es?campaign=${campaigns[0].code}`);
    }
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
   * The filters narrow the list the shopper is looking at, the way a click does
   * (F-5): the address holds the choice and the chip shows it. Where there is no
   * list, the tools say so rather than narrow something nobody can see.
   */
  test('narrows the list on screen by a facet it read from that list', async ({ page }) => {
    await withAgent(page);
    expect((await call(page, 'list_filters', {})).isError).toBe(true);

    await page.goto('/es/c/SHOES');
    await expect(page.locator('.toolbar__count[role="status"]')).toContainText(/\d/);

    const facets = await call(page, 'list_filters', {});
    expect(facets.isError).toBe(false);
    const brand = /^- brand \([^)]*\): (\S+) \[/m.exec(facets.text)?.[1];
    expect(brand).toBeTruthy();

    const narrowed = await call(page, 'apply_filter', { facet: 'brand', values: [brand as string] });
    expect(narrowed.isError).toBe(false);
    expect(narrowed.text).toMatch(/^\d+ product\(s\) now on screen/);
    await expect(page).toHaveURL(new RegExp(`[?&]brand=${brand as string}`));
    await expect(page.locator('.chips .chip')).toHaveText([brand as string]);

    // A label is not a value: the refusal names what to send instead.
    const wrong = await call(page, 'apply_filter', { facet: 'brand', values: ['nobody'] });
    expect(wrong.isError).toBe(true);
    expect(wrong.text).toContain('Its options:');
  });

  /**
   * `P6-5`, in the browser. Without `document.modelContext` nothing registers,
   * nothing throws and the shop is unchanged — which is what "degrades
   * gracefully" is supposed to mean and rarely does.
   */
  test('is simply absent on a browser that cannot host an agent', async ({ page }) => {
    const failures: string[] = [];
    page.on('pageerror', (error) => failures.push(String(error)));

    await page.goto('/es');

    await expect(page.getByRole('searchbox')).toBeVisible();
    expect(await page.evaluate(() => 'modelContext' in document)).toBe(false);
    expect(failures).toEqual([]);
  });
});
