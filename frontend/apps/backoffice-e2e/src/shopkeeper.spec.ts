import { expect, test } from '@playwright/test';
import { SHOPKEEPER, SHOPKEEPER_SUBJECT, signIn } from './session';

/**
 * What a shopkeeper does, in a browser.
 *
 * The whole commerce loop is already covered by `CheckoutLoopTests` against a
 * real Postgres and the real outbox — that an order ships, captures and restocks
 * is not this file's job, and asserting it here again would be a slower copy of
 * a test that already exists.
 *
 * What only a browser can prove is the three things below, and they are the
 * three that have actually broken in this repository:
 *
 * 1. The route guard, the token interceptor and the development issuer work
 *    together. A restart invalidates every token in a browser — the dev issuer
 *    mints its signing key per process — and the shell has to turn that into a
 *    sign-in screen rather than a page of failed panels.
 * 2. The tabs reach the screens they name. `nav.orders` was a translated key
 *    routing nowhere for four days.
 * 3. A table derives its buttons from a status the SERVER sent, and shows the
 *    server's own refusal when it guesses wrong. `Order.AllowedTransitions` is
 *    the single source of truth; this checks the screen never pretends to be a
 *    second one.
 */

/** The seeded shopper: signed in, and not somebody the backoffice is for. */
const SHOPPER = { name: 'Ana Ruiz', subject: 'ana' };

test.describe('the backoffice', () => {
  test('asks who you are before it shows anything', async ({ page }) => {
    await page.goto('/orders');

    // The guard exists so the interface does not offer what the server is going
    // to deny: without it the page would load empty with a 401 in the console
    // and nothing to explain why.
    // **The door first, the URL second**, and the order is the fix rather than a
    // preference. `toHaveURL` was checked immediately after `goto` and gave the
    // redirect five seconds — which was plenty when the app booted on its own
    // and is not now: the initializer asks the API which issuer it trusts and
    // downloads that issuer's discovery document before the first route
    // resolves. On CI it went past five seconds and the spec read `/orders`,
    // which is the page BEFORE the guard has run rather than a guard that let
    // somebody through.
    //
    // Waiting for something the door renders waits for the application to have
    // finished deciding. The URL assertion after it is then instant, and still
    // catches a guard that sent somebody to the wrong place.
    //
    // **The button only exists if the API answered**, and making sure of that is
    // the workflow's job rather than this one's: `AuthStore.bootstrap()`
    // swallows a failed `/api/auth/config` and `SignInPage` reads the issuer
    // once, so a page loaded while the API was still starting renders the
    // no-issuer notice and never a button, at any timeout at all. CI waits for
    // the API through the backoffice's own proxy before it runs anything now.
    //
    // What is left for this line to absorb is the FIRST page load of the run —
    // a cold Vite dev server compiling on demand — which is the same cost the
    // sixty-second test budget in `playwright.config.ts` was raised for. Five
    // seconds is the default for a warm application, and this is the one
    // assertion in the suite that never meets one.
    await expect(page.getByRole('button', { name: /continue/i })).toBeVisible({
      timeout: 30_000,
    });

    await expect(page).toHaveURL(/\/sign-in(\?|$)/);
  });

  test('signs a shopkeeper in and lands on the dashboard', async ({ page }) => {
    await signIn(page);

    // The dashboard has been the backoffice's home since P11-7; this still said
    // the review queue, and failed against every stack since.
    await expect(page).toHaveURL(/\/dashboard$/);

    // Who is acting and under which role, because "why can I not publish?" is a
    // question the interface should answer before it is asked.
    await expect(page.getByText('shopkeeper')).toBeVisible();
  });

  test('every tab reaches the screen it names', async ({ page }) => {
    await signIn(page);

    for (const path of ['/catalogue', '/stock', '/orders', '/returns', '/attributes', '/promotions', '/audit']) {
      await page.goto(path);

      // A heading, not a URL: a route that resolves to a blank component would
      // pass a URL assertion. `nav.orders` was a translated key routing nowhere
      // for four days, and this is the check that would have caught it.
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    }
  });

  /**
   * The orders pane (the redesign's R-4.2): an order opens beside the list with
   * the focus on its heading, and cancelling — once one click beside Ship —
   * asks first and does nothing when the answer is to keep it (R-1, B-F5).
   */
  test('cancelling an order asks first, and keeping it changes nothing', async ({ page }) => {
    await signIn(page);
    await page.goto('/orders?status=Confirmed');

    const confirmed = page.getByRole('row').filter({ has: page.getByText(/^(Confirmed|Confirmado)$/) }).first();
    const first = confirmed.getByRole('button', { name: /^(Open order|Abrir el pedido) / });
    await expect(first).toBeVisible();
    await first.click();

    const pane = page.getByRole('complementary', { name: /^(Order|Pedido) / });
    await expect(pane.getByRole('heading', { level: 2 })).toBeFocused();
    await expect(pane.getByText(/^(Confirmed|Confirmado)$/)).toBeVisible();

    await pane.getByRole('button', { name: /Cancel order…|Cancelar pedido…/ }).click();
    const confirm = page.getByRole('dialog');
    await expect(confirm).toBeVisible();
    await confirm.getByRole('button', { name: /Keep the order|Mantener el pedido/ }).click();

    await expect(confirm).toBeHidden();
    await expect(pane.getByText(/^(Confirmed|Confirmado)$/)).toBeVisible();
  });

  /**
   * Withdrawing is undoable, so it does not ask: it offers Undo, which is
   * publishing again (the redesign's R-4.3, R-2's P12). The product ends where
   * it started, so the laboratory is left as it was.
   */
  /**
   * A suspended agent reads as suspended (W-1, the baseline walkthrough). The API
   * says `suspended` and the screen compared with `'Suspended'`, so the tag
   * stayed Active and the Suspend button stayed there. The Undo test above held
   * the toast and nothing held the row. Undo reinstates, so the laboratory is
   * left as it was.
   */
  test('a suspended agent shows as suspended until Undo reinstates it', async ({ page }) => {
    await signIn(page);
    await page.goto('/agents');

    const suspend = /^\s*(Suspend|Suspender)\s*$/;
    const reinstate = /^\s*(Reinstate|Rehabilitar)\s*$/;
    const row = page.getByRole('table').getByRole('row').filter({ has: page.getByRole('button', { name: /^\s*(Suspend|Suspender|Reinstate|Rehabilitar)\s*$/ }) }).first();
    await expect(row).toBeVisible();

    // The walkthrough's B5 leaves its agent suspended, as walkthrough-prepare
    // knows; start from an active one the same way it does.
    if (await row.getByRole('button', { name: reinstate }).isVisible()) {
      await row.getByRole('button', { name: reinstate }).click();
      await expect(row.getByRole('button', { name: suspend })).toBeVisible();
    }

    await row.getByRole('button', { name: suspend }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('textbox').fill('e2e: placing strange orders');
    await dialog.getByRole('button', { name: suspend }).click();

    await expect(row.getByText(/^\s*(Suspended|Suspendido)\s*$/)).toBeVisible();
    await expect(row.getByRole('button', { name: reinstate })).toBeVisible();

    await page.getByRole('status').getByRole('button', { name: /^(Undo|Deshacer)$/ }).click();
    await expect(row.getByText(/^\s*(Active|Activo)\s*$/)).toBeVisible();
  });

  test('taking a product off sale offers Undo, and Undo puts it back', async ({ page }) => {
    await signIn(page);
    await page.goto('/catalogue');

    const first = page.getByRole('table').getByRole('button', { expanded: false }).first();
    await first.click();

    const pane = page.getByRole('complementary', { name: /./ });
    await pane.getByRole('button', { name: /Take it off sale|Retirarlo de la venta/ }).click();

    const undo = page.getByRole('status').getByRole('button', { name: /^(Undo|Deshacer)$/ });
    await expect(undo).toBeVisible();
    await expect(pane.getByText(/^\s*(Draft|Borrador)\s*$/)).toBeVisible();

    await undo.click();
    await expect(pane.getByText(/^\s*(On sale|A la venta)\s*$/)).toBeVisible();
  });

  /**
   * The command palette (the redesign's R-4.6): Ctrl K from anywhere, a screen
   * by its name in either language, and a product by what is remembered of it.
   */
  test('the palette goes to a screen by its name, and finds a product', async ({ page }) => {
    await signIn(page);
    await page.goto('/dashboard');
    // The shortcut listens once the shell is up; a key pressed before it lands nowhere.
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await page.keyboard.press('Control+k');
    const palette = page.getByRole('dialog', { name: /Go to, or find|Ir a, o buscar/ });
    const input = palette.getByRole('combobox');
    await expect(input).toBeFocused();

    await input.fill('return');
    await expect(palette.getByRole('option').first()).toContainText(/Returns|Devoluciones/);
    await input.press('Enter');
    await expect(page).toHaveURL(/\/returns/);

    await page.keyboard.press('Control+k');
    await input.fill('mochila');
    await expect(palette.getByRole('option').filter({ hasText: /Product|Producto/ }).first()).toBeVisible();
  });

  /** j and k down a queue worked item by item, the pane following (R-4.6). */
  test('j and k move down and up the claims, the pane following', async ({ page }) => {
    await signIn(page);
    await page.goto('/claims?support=all');
    await expect(page.getByRole('table')).toBeVisible();

    await page.keyboard.press('j');
    await expect(page).toHaveURL(/claim=/);
    const first = new URL(page.url()).searchParams.get('claim');

    await page.keyboard.press('j');
    await expect(page).not.toHaveURL(new RegExp(`claim=${first}`));

    await page.keyboard.press('k');
    await expect(page).toHaveURL(new RegExp(`claim=${first}`));
  });

  test('a stocktake goes through the ledger and comes back recomputed', async ({ page }) => {
    await signIn(page);
    await page.goto('/stock');

    const row = page.getByRole('row').filter({ hasText: 'B073WXYZ01-DEFAULT' }).first();
    const count = row.getByRole('spinbutton');

    // **A count DIFFERENT from the one that is there**, computed rather than
    // typed. The first version filled in a literal 7, which passed once and
    // then failed on every later run against the same database: the save button
    // only appears on a row that CHANGED, and by the second run the row already
    // said 7. A stateful spec with a constant in it is a spec that tests the
    // database's history.
    const before = Number((await count.inputValue()) || '0');
    const after = String(before === 7 ? 4 : 7);

    await count.fill(after);

    // The save button only appears on a row that changed — a count is not an
    // increment, and pressing save on an unchanged row would be a write nobody
    // asked for.
    await row.getByRole('button').click();

    // Availability is DERIVED server-side (`onHand - reserved`), so the screen
    // redraws from the answer rather than from what was typed. A client that
    // subtracted for itself would disagree with the search index the moment a
    // reservation existed.
    await expect(row).toContainText(after);
  });

  /**
   * R-7.1: the dashboard's figure opens the stock exactly there, with the same
   * number, and a SKU is found by its product's name — the catalogue's half of
   * the search — as well as by the SKU itself, the ledger's.
   */
  test('the stock opens on what to restock, and finds a SKU by its product name or its code', async ({ page }) => {
    await signIn(page);
    await page.goto('/dashboard');

    const figure = page.getByRole('link', { name: /to restock|por reponer/i });
    // A number, not the dash the widget shows until its answer arrives.
    await expect(figure.locator('.task__count')).toHaveText(/^\s*\d+\s*$/);
    const shown = (await figure.locator('.task__count').innerText()).trim();
    await figure.click();

    await expect(page).toHaveURL(/filter=Restock/);
    const chip = page.getByRole('button', { name: /^\s*(to restock|por reponer)/i });
    await expect(chip).toHaveAttribute('aria-pressed', 'true');
    await expect(chip).toContainText(shown);

    await page.getByRole('button', { name: /^\s*(all|todo)/i }).click();

    // A SKU the catalogue names — some seed SKUs belong to no product, which
    // the screen shows as such — found by its product's first word, then by
    // part of the SKU itself.
    const named = page.locator('tr').filter({ has: page.locator('.product__name') }).first();
    await expect(named).toBeVisible();
    const sku = (await named.locator('td').nth(1).innerText()).trim();
    const word = (await named.locator('.product__name').innerText()).trim().split(/\s+/)[0];

    const search = page.getByRole('searchbox');
    await search.fill(word);
    await expect(page).toHaveURL(new RegExp(`q=${encodeURIComponent(word)}`));
    await expect(page.getByRole('row').filter({ hasText: sku }).first()).toBeVisible();

    await search.fill(sku.slice(0, 6).toLowerCase());
    await expect(page.getByRole('row').filter({ hasText: sku }).first()).toBeVisible();

    await search.fill('zzz-nothing-zzz');
    await expect(page.getByText(/nothing matches|nada coincide/i)).toBeVisible();
  });

  /**
   * R-7.2: a list longer than a page says how long it is and walks it, instead
   * of stopping at fifty and saying nothing. The page is in the URL, so page two
   * is a link; choosing another filter starts again at the first.
   */
  test('a long list says how long it is and pages through it', async ({ page }) => {
    await signIn(page);
    await page.goto('/orders?status=all');

    const pager = page.getByRole('navigation', { name: /pages|páginas/i });
    const range = pager.getByRole('status');
    await expect(range).toHaveText(/^\s*1–20 (of|de) \d+\s*$/);
    const total = Number((await range.innerText()).trim().split(/\s+/).pop());
    test.skip(total <= 20, 'The laboratory has twenty orders or fewer.');

    // The ids on the page, not its text: a page two that repeated page one would
    // still say 21–40, because the range is the pager's arithmetic.
    const ids = () => page.locator('tbody tr button[title]').evaluateAll((buttons) => buttons.map((b) => b.getAttribute('title')));
    const firstPage = await ids();
    await pager.getByRole('button', { name: /next|siguiente/i }).click();
    await expect(page).toHaveURL(/page=2/);
    await expect(range).toHaveText(new RegExp(`^\\s*21–${Math.min(40, total)} (of|de) ${total}\\s*$`));
    await expect.poll(async () => (await ids()).some((id) => firstPage.includes(id))).toBe(false);

    await page.getByRole('button', { name: /^\s*(to ship|por enviar)/i }).click();
    await expect(page).not.toHaveURL(/page=/);
  });

  /**
   * Publishing, the journey the traffic simulator's shopkeeper walks (ADR 0037).
   * A draft leaves the queue and is on sale.
   *
   * Conditional, and said so: importing never publishes, and re-importing the
   * seed updates what exists rather than making drafts again, so a laboratory
   * whose catalogue was published has an empty queue — a legitimate state, the
   * same one the orders test below allows for.
   */
  test('publishing a draft takes it out of the queue', async ({ page }) => {
    await signIn(page);
    await page.goto('/review');

    const empty = page.getByText(/Nothing is waiting for review|No hay nada pendiente/);
    const publish = page.getByRole('button', { name: /^(publish|publicar)$/i });
    await expect(empty.or(publish.first())).toBeVisible();
    if (await empty.isVisible()) test.skip(true, 'The review queue is empty: every product is already on sale.');

    const before = await publish.count();
    await publish.first().click();

    await expect(publish).toHaveCount(before - 1);
  });

  test('the orders table offers only the moves the state machine allows', async ({ page }) => {
    await signIn(page);
    await page.goto('/orders');

    const empty = page.getByText(/No orders yet|Todavía no hay pedidos/);

    // A shop with no orders is a legitimate state and the reason this is not an
    // unconditional assertion: the fixture is whatever the stack has, and a spec
    // that demanded an order would fail on a fresh clone for the wrong reason.
    if (await empty.isVisible().catch(() => false)) test.skip();

    const row = page.getByRole('row').nth(1);
    const buttons = row.getByRole('button');

    // Whatever the status is, the row offers at most the moves that status
    // allows — never both "ship" and "delivered", which is the pair a screen
    // keeping its own copy of the table gets wrong first.
    const labels = await buttons.allInnerTexts();

    expect(labels.filter((label) => /Ship|Enviar/.test(label)).length).toBeLessThanOrEqual(1);
    expect(labels.filter((label) => /Delivered|Entregado/.test(label)).length).toBeLessThanOrEqual(1);
  });

  /**
   * An expired session is not a server that went quiet, and the interface must
   * not say it was.
   *
   * The development issuer mints its signing key per process, so every restart
   * of the API ends every session in every browser — which makes this the most
   * common failure a person meets here, and it used to be rendered as "the
   * queue did not answer, try again in a moment". A 401 has to reach the door
   * with the reason attached.
   *
   * Only a browser can check it: the status is the server's, the sentence is
   * the screen's, and nothing below this layer sees both.
   */
  test('a session that ended sends you to the door, and the door says why', async ({ page }) => {
    await signIn(page);
    await page.goto('/review');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    // What an expired token looks like from the browser's side.
    await page.route('**/api/**', (route) => route.fulfill({ status: 401, body: '' }));

    // The interruption happens on a screen that is NOT where signing in lands
    // by default. On the review queue the return trip would pass without
    // carrying anything, which is a test that asserts a coincidence.
    await page.getByRole('link', { name: /orders|pedidos/i }).click();

    await expect(page).toHaveURL(/\/sign-in\?.*reason=expired/);
    await expect(page.getByText(/session expired|sesión ha caducado/i)).toBeVisible();

    // And NOT the message that blamed the server for it.
    await expect(page.getByText(/did not answer|no ha respondido/i)).toHaveCount(0);

    // The way back travels in the same parameter the guard uses. The copy on
    // this screen promises it, so it is asserted rather than assumed.
    await expect(page).toHaveURL(/returnTo=%2Forders/);

    await page.unroute('**/api/**');
    await page.getByRole('button', { name: /continue/i }).click();
    if (/\/realms\//.test(page.url())) {
      await page.locator('input[name="username"]').fill(SHOPKEEPER_SUBJECT);
      await page.locator('input[name="password"]').fill(SHOPKEEPER_SUBJECT);
      await page.locator('input[type="submit"], button[type="submit"]').first().click();
    } else {
      await page.getByRole('link', { name: SHOPKEEPER }).click();
    }

    await expect(page).toHaveURL(/\/orders$/);
  });

  /**
   * The guard asks about the ROLE, not only about a session. The first version
   * asked whether anybody was signed in, and the UI review of 2026-09-11 walked
   * in as a shopper: every tab, an import button the API refuses, and each 403
   * rendered as "could not be loaded". Only a browser sees a guard, so this is
   * the one layer that can hold it.
   */
  test('turns a shopper away at the door instead of letting them in to be refused', async ({ page }) => {
    await signIn(page, SHOPPER);

    await expect(page).toHaveURL(/\/sign-in\?.*reason=role/);
    await expect(page.getByRole('status')).toContainText(/shopper/);

    // No tabs: a bar offering seven screens to somebody every one of them turns
    // away is the door this closed.
    await expect(page.getByRole('link', { name: /review queue|cola de revisión/i })).toHaveCount(0);

    // Typing a screen's address meets the same door.
    await page.goto('/orders');
    await expect(page).toHaveURL(/\/sign-in\?.*reason=role/);
  });

  test('signing out sends you back to the door', async ({ page }) => {
    await signIn(page);

    // Signing out lives in the account menu since the redesign (R-3), beside the
    // language and the theme.
    await page.getByRole('button', { name: /^(Account|Cuenta):/ }).click();
    await page.getByRole('button', { name: /Sign out|Cerrar sesión/ }).click();

    await expect(page).toHaveURL(/\/sign-in(\?|$)/);

    // And the token is gone, not merely hidden: navigating straight back must
    // meet the guard again.
    await page.goto('/orders');
    await expect(page).toHaveURL(/\/sign-in(\?|$)/);
  });
});
