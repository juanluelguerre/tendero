import { expect, test, type Page } from '@playwright/test';
import { signIn } from './session';

/**
 * Gets the laboratory shop ready for the redesign's walkthrough
 * (docs/specs/redesign-walkthrough.md, *Before the session*), and reports what
 * each job has to act on. It changes the shop, so it runs only when asked:
 *
 *   WALKTHROUGH_PREPARE=1 npx playwright test -c apps/backoffice-e2e/playwright.config.ts walkthrough-prepare
 *
 * Run it after TrafficSim has stopped: the simulated shopkeeper publishes every
 * draft it finds. Drafts come from WithdrawProduct, the shop's own operation,
 * never from the database by hand — so the index, the audit and the metrics all
 * see what happened. `WALKTHROUGH_DRAFTS` sets how many (default 3).
 */
test.skip(!process.env['WALKTHROUGH_PREPARE'], 'changes the shop; set WALKTHROUGH_PREPARE=1 to run it');

interface ProductSummary { productId: string; code: string; name: string; category: string | null; status: string }
interface OrderSummary { orderId: string; status: string; recipientName: string; createdAt: string }
interface ReturnView { number: number; status: string }
interface AgentView { agentId: string; displayName: string; status: string }

const is = (status: string, expected: string) => status.toLowerCase() === expected.toLowerCase();

/** S1 asks for the backpacks, so none of them is taken off sale. */
const kept = (product: ProductSummary) => !/backpack|mochila/i.test(`${product.category} ${product.name}`);

test('prepare the shop for the walkthrough', async ({ page }) => {
  test.setTimeout(120_000);
  const api = await shopkeeperApi(page);
  const wanted = Number(process.env['WALKTHROUGH_DRAFTS'] ?? 3);

  // A hundred per page is the endpoint's ceiling; the laboratory fits in two.
  const products: ProductSummary[] = [];
  for (let page = 1; ; page++) {
    const { items, total } = await api.get<{ items: ProductSummary[]; total: number }>(`/api/catalog/products?pageSize=100&page=${page}`);
    products.push(...items);
    if (items.length === 0 || products.length >= total) break;
  }
  const drafts = products.filter((p) => is(p.status, 'draft'));
  const candidates = products
    .filter((p) => is(p.status, 'active') && kept(p))
    .sort((a, b) => b.code.localeCompare(a.code))
    .slice(0, Math.max(0, wanted - drafts.length));
  for (const product of candidates) {
    const { status } = await api.post<{ status: string }>(`/api/catalog/products/${product.productId}/withdraw`);
    expect(status).toBe('draft');
  }

  const claims = await api.get<unknown[]>('/api/knowledge/claims?limit=50');
  const orders = (await api.get<{ orders: OrderSummary[] }>('/api/orders')).orders.filter((o) => is(o.status, 'confirmed'));
  const returns = (await api.get<ReturnView[]>('/api/returns')).filter((r) => is(r.status, 'requested'));
  const agents = await activeAgent(api);

  const report: [string, number, string][] = [
    ['B1 drafts to review', drafts.length + candidates.length, candidates.map((p) => `withdrawn ${p.code}`).join(', ')],
    ['B2 claims waiting', claims.length, ''],
    ['B3 confirmed orders', orders.length, orders[0] ? `card: ${orders[0].orderId.slice(0, 8)} (${orders[0].recipientName})` : ''],
    ['B4 open returns', returns.length, returns[0] ? `first: #${returns[0].number}` : ''],
    ['B5 active agents', agents.length, agents[0] ? `card: ${agents[0].displayName}` : ''],
  ];
  console.log(report.map(([job, n, note]) => `${n > 0 ? 'ok     ' : 'MISSING'} ${job}: ${n} ${note}`).join('\n'));

  const missing = report.filter(([, n]) => n === 0).map(([job]) => job);
  expect(missing, 'Nothing to act on — run TrafficSim for longer, then prepare again').toEqual([]);
});

/**
 * B5 needs an agent on sale's side of the door. A suspended one is reinstated,
 * and with none registered, the development issuer's agent is — the one whose
 * token MCP and UCP clients carry in the laboratory. Both are the shop's own
 * operations, as the backoffice's Agents screen performs them.
 */
async function activeAgent(api: ShopkeeperApi): Promise<AgentView[]> {
  const all = await api.get<AgentView[]>('/api/agents');
  const active = all.filter((a) => is(a.status, 'active'));
  if (active.length > 0) return active;

  const suspended = all.find((a) => is(a.status, 'suspended'));
  if (suspended) await api.post(`/api/agents/${suspended.agentId}/reinstate`);
  else await api.post('/api/agents', { agentId: 'claude-desktop', displayName: 'Claude Desktop', operator: 'Anthropic' });
  return (await api.get<AgentView[]>('/api/agents')).filter((a) => is(a.status, 'active'));
}

type ShopkeeperApi = Awaited<ReturnType<typeof shopkeeperApi>>;

/**
 * Signs in through the screen, as the specs do, and borrows the token the app
 * sends: the preparation acts as the shopkeeper, not around them.
 */
async function shopkeeperApi(page: Page) {
  let authorization: string | undefined;
  let origin: string | undefined;
  page.on('request', (request) => {
    const header = request.headers()['authorization'];
    if (!authorization && header && request.url().includes('/api/')) {
      authorization = header;
      origin = new URL(request.url()).origin;
    }
  });
  await signIn(page);
  await page.goto('/orders');
  await expect.poll(() => authorization, { message: 'the backoffice never called the API with a token' }).toBeTruthy();

  const headers = () => ({ authorization: authorization ?? '', accept: 'application/json' });
  return {
    async get<T>(path: string): Promise<T> {
      const response = await page.request.get(`${origin}${path}`, { headers: headers() });
      expect(response.ok(), `GET ${path}: ${response.status()}`).toBeTruthy();
      return response.json();
    },
    async post<T>(path: string, data?: unknown): Promise<T> {
      const response = await page.request.post(`${origin}${path}`, { headers: headers(), data });
      expect(response.ok(), `POST ${path}: ${response.status()}`).toBeTruthy();
      return response.json();
    },
  };
}
