import type { TinoContext } from '@tendero/shared-api';
import type { TrackedEvents } from '@tendero/shared-telemetry';

/** The kind of page the shopper is on, as the tracking plan names them. */
export type TinoPage = TrackedEvents['tino_opened']['page'];

const Product = /^\/[a-z]{2}\/p\/[^/?#]+\/([^/?#]+)/;
const Order = /^\/[a-z]{2}\/orders\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?:[/?#]|$)/i;

/**
 * What the shopper is looking at, for a question that says "it" (the Tino spec,
 * *The conversation*): a product's page or an order's, read from the address
 * and sent beside the question. Only where it carries something — on the home,
 * a listing or the cart there is no "it", so there is no context.
 *
 * An order's id is the one thing here that is a credential, for a guest: it
 * goes to the server beside the question and never into the conversation.
 */
export function contextOf(url: string): TinoContext | null {
  const product = Product.exec(url);
  if (product) return { kind: 'Product', key: decodeURIComponent(product[1]) };

  const order = Order.exec(url);
  return order ? { kind: 'Order', key: order[1] } : null;
}

/** The kind of page an address is, for `tino_opened`: where the door is used from. */
export function pageOf(url: string): TinoPage {
  const [path, query = ''] = url.split('?');
  if (Product.test(path)) return 'product';
  if (/^\/[a-z]{2}\/orders\//.test(path)) return 'order';
  if (/^\/[a-z]{2}\/c\//.test(path)) return 'category';
  if (/^\/[a-z]{2}\/cart(?:\/|$)/.test(path)) return 'cart';
  if (/^\/[a-z]{2}\/?$/.test(path)) return new URLSearchParams(query).has('q') ? 'search' : 'home';
  return 'other';
}
