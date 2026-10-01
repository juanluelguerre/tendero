import { expect, type Page } from '@playwright/test';
import { signIn } from './session';

/**
 * The laboratory shop the specs act on, made by the shop's own operations.
 *
 * A stack brought up from nothing — CI's, or a fresh clone's — has the stock
 * seed and nothing else: no catalogue, no orders, no agents, no claims. The
 * specs used to assume the author's laboratory, where TrafficSim had filled all
 * of that, and eight of them failed on every other machine for that reason
 * alone. `prepareLaboratory` makes the least each of them needs, and only what
 * is missing, so it changes nothing in a laboratory that already has it.
 *
 * Everything goes through the API a person would use — importing, publishing,
 * a guest's checkout, registering an agent — never the database: the outbox,
 * the index and the audit see what happened, as they would in the shop.
 */

export interface ProductSummary {
  productId: string;
  code: string;
  name: string;
  category: string | null;
  status: string;
}
interface AgentView {
  agentId: string;
  displayName: string;
  status: string;
}
interface StockRow {
  sku: string;
  available: number;
}

/** A status as the API spells it, whatever its case. */
export const is = (status: string, expected: string) => status.toLowerCase() === expected.toLowerCase();

/** What the specs need, and why each: the smallest shop in which every one of them has something to act on. */
export const Needs = {
  /** On sale: the catalogue opens on them, the palette finds them, and orders can only buy them. */
  onSale: 3,
  /** More than a page of orders: the pager walks to page two only when there is one. */
  orders: 21,
  /** j and k need somewhere to go from the first claim. */
  claims: 2,
} as const;

/**
 * The seeded shopkeeper's API: signs in through the screen, as the specs do, and
 * borrows the token the app sends — acting as the shopkeeper, not around them.
 */
export async function shopkeeperApi(page: Page) {
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

  const send = async <T>(
    method: 'get' | 'post',
    path: string,
    data?: unknown,
    headers: Record<string, string> = {},
  ) => {
    const response = await page.request[method](`${origin}${path}`, {
      headers: { accept: 'application/json', ...headers },
      data,
    });
    expect(
      response.ok(),
      `${method.toUpperCase()} ${path}: ${response.status()} ${await response.text()}`,
    ).toBeTruthy();
    return { body: (await response.json()) as T, headers: response.headers() };
  };
  const asShopkeeper = () => ({ authorization: authorization ?? '' });

  return {
    get: async <T>(path: string) => (await send<T>('get', path, undefined, asShopkeeper())).body,
    post: async <T>(path: string, data?: unknown) => (await send<T>('post', path, data, asShopkeeper())).body,
    /** A guest's request: no token, as the storefront's checkout sends it; its headers carry the cart's. */
    guest: <T>(path: string, data: unknown, headers: Record<string, string> = {}) =>
      send<T>('post', path, data, headers),
  };
}

export type ShopkeeperApi = Awaited<ReturnType<typeof shopkeeperApi>>;

/** Every product, a hundred at a time — the endpoint's ceiling. */
export async function allProducts(api: ShopkeeperApi): Promise<ProductSummary[]> {
  const products: ProductSummary[] = [];
  for (let page = 1; ; page++) {
    const { items, total } = await api.get<{ items: ProductSummary[]; total: number }>(
      `/api/catalog/products?pageSize=100&page=${page}`,
    );
    products.push(...items);
    if (items.length === 0 || products.length >= total) return products;
  }
}

/**
 * An agent on the shop's side of the door. A suspended one is reinstated, and
 * with none registered, the development issuer's agent is — the one whose token
 * MCP and UCP clients carry in the laboratory. Both are the shop's own
 * operations, as the backoffice's Agents screen performs them.
 */
export async function activeAgent(api: ShopkeeperApi): Promise<AgentView[]> {
  const all = await api.get<AgentView[]>('/api/agents');
  const active = all.filter((a) => is(a.status, 'active'));
  if (active.length > 0) return active;

  const suspended = all.find((a) => is(a.status, 'suspended'));
  if (suspended) await api.post(`/api/agents/${suspended.agentId}/reinstate`);
  else
    await api.post('/api/agents', { agentId: 'claude-desktop', displayName: 'Claude Desktop', operator: 'Anthropic' });
  return (await api.get<AgentView[]>('/api/agents')).filter((a) => is(a.status, 'active'));
}

/** The seed catalogue, imported once: importing never publishes (ADR 0012), so it arrives as drafts. */
async function catalogue(api: ShopkeeperApi): Promise<ProductSummary[]> {
  const { total } = await api.get<{ total: number }>('/api/catalog/products?pageSize=1');
  if (total === 0) await api.post('/api/catalog/import', { source: 'seed' });
  return allProducts(api);
}

/** A SKU with what is left of it across the warehouses, and the product it belongs to. */
interface Stocked {
  sku: string;
  available: number;
  productCode: string;
}

/**
 * The SKUs with stock, most left first, each with its product: the stock names
 * SKUs and the catalogue names products, and the catalogue says which is which.
 */
async function stocked(api: ShopkeeperApi): Promise<Stocked[]> {
  const { rows } = await api.get<{ rows: StockRow[] }>('/api/inventory/stock?filter=All&pageSize=100');
  const left = new Map<string, number>();
  for (const row of rows) left.set(row.sku, (left.get(row.sku) ?? 0) + row.available);
  const skus = [...left]
    .filter(([, available]) => available > 0)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 20);
  const query = skus.map(([sku]) => `sku=${encodeURIComponent(sku)}`).join('&');
  const { items } = await api.get<{ items: { sku: string; productCode: string }[] }>(`/api/catalog/skus?${query}`);
  const productOf = new Map(items.map((item) => [item.sku, item.productCode]));
  return skus.flatMap(([sku, available]) =>
    productOf.has(sku) ? [{ sku, available, productCode: productOf.get(sku) ?? '' }] : [],
  );
}

/**
 * Enough on sale to buy from: the drafts with the most stock are published first.
 * A draft the shop refuses to publish — a translation missing, say — is the
 * shop's answer, not the laboratory's problem: the next one is tried.
 */
async function onSale(api: ShopkeeperApi, products: ProductSummary[], stock: Stocked[]): Promise<Stocked[]> {
  const byCode = new Map(products.map((product) => [product.code, product]));
  const selling = new Set(products.filter((product) => is(product.status, 'active')).map((product) => product.code));
  const buyable = () => stock.filter((item) => selling.has(item.productCode));
  for (const { productCode } of stock) {
    if (buyable().length >= Needs.onSale) break;
    const draft = byCode.get(productCode);
    if (!draft || !is(draft.status, 'draft') || selling.has(productCode)) continue;
    const response = await api
      .post<{ status: string }>(`/api/catalog/products/${draft.productId}/publish`)
      .catch(() => null);
    if (response && is(response.status, 'active')) selling.add(productCode);
  }
  expect(buyable().length, 'SKUs on sale with stock to buy').toBeGreaterThanOrEqual(Needs.onSale);
  return buyable();
}

const Address = {
  recipientName: 'Laboratorio E2E',
  line1: 'Calle Mayor 1',
  line2: null,
  city: 'Madrid',
  region: null,
  postalCode: '28013',
  countryCode: 'ES',
  phone: null,
};

/**
 * One guest's order, as the storefront's checkout places it: a cart, its quote,
 * a shipping option for the address, and the fake provider's card that
 * authorises. The stock saga confirms it off the outbox.
 */
async function placeOrder(api: ShopkeeperApi, sku: string): Promise<void> {
  // The cart's token comes back in a header on every answer, as the storefront reads it.
  const cart = await api.guest('/api/cart/lines', { sku, quantity: 1 });
  const headers = { 'X-Cart-Token': cart.headers['x-cart-token'] };
  // The shipping first: the quote's hash covers it, and a quote without it is a price that changed.
  const {
    options: [option],
  } = (
    await api.guest<{ options: { code: string; amount: number }[] }>('/api/checkout/shipping-options', Address, headers)
  ).body;
  const quote = await api.guest<{ inputHash: string }>(
    '/api/pricing/quote',
    { lines: [{ sku, quantity: 1 }], shipping: option.amount },
    headers,
  );
  await api.guest(
    '/api/checkout',
    {
      shippingAddress: Address,
      shippingOptionCode: option.code,
      quoteHash: quote.body.inputHash,
      instrument: 'card-ok',
      idempotencyKey: `e2e-${sku}-${Date.now()}-${Math.random().toString(36).slice(2)}`,
    },
    headers,
  );
}

/** Waits for the stock saga: an order placed is authorised at once and confirmed off the outbox. */
async function confirmed(api: ShopkeeperApi): Promise<number> {
  let count = 0;
  await expect
    .poll(async () => (count = (await api.get<{ total: number }>('/api/orders?status=Confirmed&pageSize=1')).total), {
      message: 'no order was confirmed: is the workers’ stock saga running?',
      timeout: 60_000,
    })
    .toBeGreaterThan(0);
  return count;
}

/**
 * Enough orders to page through, and at least one confirmed — the one a spec
 * opens and then keeps. Each buys one unit, never more than a SKU has left, so
 * the specs never run the laboratory's stock out.
 */
async function orders(api: ShopkeeperApi, buyable: Stocked[]): Promise<void> {
  const [{ total }, { total: confirmedAlready }] = await Promise.all([
    api.get<{ total: number }>('/api/orders?pageSize=1'),
    api.get<{ total: number }>('/api/orders?status=Confirmed&pageSize=1'),
  ]);
  const missing = Math.max(Needs.orders - total, confirmedAlready === 0 ? 1 : 0);
  if (missing === 0) return;

  // One unit of each SKU in turn, never more than it has left.
  const rounds = Math.max(...buyable.map((item) => item.available));
  const units = Array.from({ length: rounds }, (_, round) =>
    buyable.filter((item) => item.available > round).map((item) => item.sku),
  ).flat();
  expect(units.length, 'units on sale to buy').toBeGreaterThanOrEqual(missing);
  for (const sku of units.slice(0, missing)) await placeOrder(api, sku);
  await confirmed(api);
}

/** Claims come from the workers reading each imported description — by rules when no model runs (invariant 8). */
async function claims(api: ShopkeeperApi): Promise<void> {
  await expect
    .poll(async () => (await api.get<{ total: number }>('/api/knowledge/claims?support=All&pageSize=1')).total, {
      message: 'no claims were proposed: are the workers reading the imported descriptions?',
      timeout: 90_000,
    })
    .toBeGreaterThanOrEqual(Needs.claims);
}

/** The smallest shop in which every spec has something to act on, made only where it is missing. */
export async function prepareLaboratory(page: Page): Promise<void> {
  const api = await shopkeeperApi(page);
  const products = await catalogue(api);
  const buyable = await onSale(api, products, await stocked(api));
  await orders(api, buyable);
  await activeAgent(api);
  await claims(api);
}
