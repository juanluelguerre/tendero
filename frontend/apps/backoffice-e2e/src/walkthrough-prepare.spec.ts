import { expect, test } from '@playwright/test';
import { orderReference } from '@tendero/shared-util';
import { activeAgent, allProducts, is, type ProductSummary, shopkeeperApi } from './laboratory';

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

interface OrderSummary {
  orderId: string;
  status: string;
  recipientName: string;
  createdAt: string;
}
interface ReturnView {
  number: number;
  status: string;
}

/** S1 asks for the backpacks, so none of them is taken off sale. */
const kept = (product: ProductSummary) => !/backpack|mochila/i.test(`${product.category} ${product.name}`);

test('prepare the shop for the walkthrough', async ({ page }) => {
  test.setTimeout(120_000);
  const api = await shopkeeperApi(page);
  const wanted = Number(process.env['WALKTHROUGH_DRAFTS'] ?? 3);

  const products = await allProducts(api);
  const drafts = products.filter((p) => is(p.status, 'draft'));
  const candidates = products
    .filter((p) => is(p.status, 'active') && kept(p))
    .sort((a, b) => b.code.localeCompare(a.code))
    .slice(0, Math.max(0, wanted - drafts.length));
  for (const product of candidates) {
    const { status } = await api.post<{ status: string }>(`/api/catalog/products/${product.productId}/withdraw`);
    expect(status).toBe('Draft');
  }

  const claims = (await api.get<{ total: number }>('/api/knowledge/claims?pageSize=1')).total;
  // The server's counts and not a page's rows: the lists page since R-7.2.
  const confirmed = await api.get<{ orders: OrderSummary[]; total: number }>('/api/orders?status=Confirmed&pageSize=1');
  const returns = (await api.get<ReturnView[]>('/api/returns')).filter((r) => is(r.status, 'requested'));
  const agents = await activeAgent(api);

  const report: [string, number, string][] = [
    ['B1 drafts to review', drafts.length + candidates.length, candidates.map((p) => `withdrawn ${p.code}`).join(', ')],
    ['B2 claims waiting', claims, ''],
    [
      'B3 confirmed orders',
      confirmed.total,
      confirmed.orders[0]
        ? `card: ${orderReference(confirmed.orders[0].orderId)} (${confirmed.orders[0].recipientName})`
        : '',
    ],
    ['B4 open returns', returns.length, returns[0] ? `first: #${returns[0].number}` : ''],
    ['B5 active agents', agents.length, agents[0] ? `card: ${agents[0].displayName}` : ''],
  ];
  console.log(report.map(([job, n, note]) => `${n > 0 ? 'ok     ' : 'MISSING'} ${job}: ${n} ${note}`).join('\n'));

  const missing = report.filter(([, n]) => n === 0).map(([job]) => job);
  expect(missing, 'Nothing to act on — run TrafficSim for longer, then prepare again').toEqual([]);
});
