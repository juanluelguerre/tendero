import { expect, type Page, request, test } from '@playwright/test';

/**
 * An imported customer's first visit (phase 16, P16-17; ADR 0067): the shop asked
 * Keycloak for an identity with their email and no password, and the person sets
 * one with Keycloak's own *forgot password*, whose link proves the address is
 * theirs. What only a browser can prove is the chain: the shop's door leads to the
 * issuer, the issuer emails a link, the link sets a password, and the shop lets the
 * person in. That the identity is linked to the imported customer is the
 * integration tests' (`ShopifyHistoryRoundTripTests`).
 *
 * **Keycloak and Mailpit only.** The development issuer provisions nobody, on
 * purpose (ADR 0067), so against it there is nothing to test and the spec says
 * so instead of failing. CI runs the development issuer; this runs on a machine
 * with `Tendero__Issuer=keycloak` (or the `https-all` profile), which starts both.
 *
 * The identity is made the way `KeycloakIdentityProvisioner` makes one — the
 * username is the email, enabled, a name and a language, no credential — through
 * the same service account and its committed development secret, and removed at
 * the end with its email.
 */
const keycloakUrl = process.env['KEYCLOAK_URL'] ?? 'https://localhost:38443';
const mailpitUrl = process.env['MAILPIT_URL'] ?? 'http://localhost:38025';
const realm = 'tendero';

const person = {
  email: `primera-visita-${Date.now()}@example.com`,
  firstName: 'Lucía',
  lastName: 'Prado',
};

let identity: string | null = null;

test.describe('an imported customer, on their first visit', () => {
  test.beforeAll(async () => {
    const reachable = await bothReachable();
    test.skip(!reachable, 'needs Keycloak and Mailpit: run the stack with Tendero__Issuer=keycloak');

    identity = await provision();
  });

  test.afterAll(async () => {
    if (identity) await removeIdentity(identity);
    await removeMail();
  });

  test('sets a password through the emailed link and is signed in to the shop', async ({ page }) => {
    await page.goto('/es/account');
    await page.getByRole('button', { name: 'Identifícate' }).click();
    await expect(page).toHaveURL(/\/realms\//);

    // By the link's target, not its words: Keycloak translates them, and the
    // shop's theme dresses them (ADR 0065).
    await page.locator('a[href*="reset-credentials"]').click();
    await page.locator('input[name="username"]').fill(person.email);
    await page.locator('input[type="submit"], button[type="submit"]').first().click();

    await page.goto(await resetLink());
    await setPassword(page, 'una-contraseña-larga');

    await expect(page.getByRole('heading', { level: 1, name: `${person.firstName} ${person.lastName}` })).toBeVisible();
  });
});

/** The new password, twice; then back to the shop if Keycloak stops to say so. */
async function setPassword(page: Page, password: string): Promise<void> {
  await page.locator('input[name="password-new"]').fill(password);
  await page.locator('input[name="password-confirm"]').fill(password);
  await page.locator('input[type="submit"], button[type="submit"]').first().click();

  // Followed in the browser that asked for the link, Keycloak finishes the
  // sign-in it interrupted and sends the person back to the shop. Should it stop
  // on its own page instead, its one link leads back.
  if (/\/realms\//.test(page.url())) {
    const back = page.locator('a[href*="redirect_uri"], a[href^="http://localhost"]').first();
    if (await back.isVisible()) await back.click();
  }
}

async function bothReachable(): Promise<boolean> {
  const api = await request.newContext({ ignoreHTTPSErrors: true });
  try {
    const keycloak = await api.get(`${keycloakUrl}/realms/${realm}/.well-known/openid-configuration`, {
      timeout: 5_000,
    });
    const mailpit = await api.get(`${mailpitUrl}/api/v1/info`, { timeout: 5_000 });
    return keycloak.ok() && mailpit.ok();
  } catch {
    return false;
  } finally {
    await api.dispose();
  }
}

async function adminApi() {
  const api = await request.newContext({ ignoreHTTPSErrors: true });
  const token = await api.post(`${keycloakUrl}/realms/${realm}/protocol/openid-connect/token`, {
    form: {
      grant_type: 'client_credentials',
      client_id: 'tendero-provisioning',
      // The development secret, committed in the realm and the AppHost on purpose
      // (keycloak/README.md, *Imported customers*).
      client_secret: 'tendero-provisioning-dev',
    },
  });
  expect(token.ok(), 'the provisioning client answers').toBe(true);
  const { access_token } = (await token.json()) as { access_token: string };
  return { api, headers: { authorization: `Bearer ${access_token}` } };
}

async function provision(): Promise<string> {
  const { api, headers } = await adminApi();
  try {
    const created = await api.post(`${keycloakUrl}/admin/realms/${realm}/users`, {
      headers,
      data: {
        username: person.email,
        email: person.email,
        firstName: person.firstName,
        lastName: person.lastName,
        enabled: true,
        attributes: { locale: ['es'] },
      },
    });
    expect(created.status(), 'Keycloak made the identity').toBe(201);
    return (created.headers()['location'] ?? '').split('/').pop() ?? '';
  } finally {
    await api.dispose();
  }
}

async function removeIdentity(id: string): Promise<void> {
  const { api, headers } = await adminApi();
  await api.delete(`${keycloakUrl}/admin/realms/${realm}/users/${id}`, { headers });
  await api.dispose();
}

/** The link in the email Keycloak sent, waited for: SMTP is a hop away. */
async function resetLink(): Promise<string> {
  const api = await request.newContext();
  try {
    let id: string | undefined;
    await expect
      .poll(
        async () => {
          const found = await api.get(`${mailpitUrl}/api/v1/search`, { params: { query: `to:"${person.email}"` } });
          id = ((await found.json()) as { messages: { ID: string }[] }).messages[0]?.ID;
          return id;
        },
        { message: 'Keycloak emailed the reset link', timeout: 30_000 },
      )
      .toBeTruthy();

    const message = (await (await api.get(`${mailpitUrl}/api/v1/message/${id}`)).json()) as { Text: string };
    const link = /https?:\/\/\S+?\/login-actions\/action-token\?\S+/.exec(message.Text)?.[0];
    if (!link) throw new Error(`The email Keycloak sent carries no action link:\n${message.Text}`);
    return link;
  } finally {
    await api.dispose();
  }
}

async function removeMail(): Promise<void> {
  const api = await request.newContext();
  await api.delete(`${mailpitUrl}/api/v1/search`, { params: { query: `to:"${person.email}"` } }).catch(() => undefined);
  await api.dispose();
}
