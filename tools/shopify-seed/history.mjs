// The lab store's people and their history (phase 16, S2b, P16-16), so the
// migration has something real to bring: five customers with their addresses,
// a dozen past orders — one cancelled, one partly refunded, one still open —
// two discounts, one the promotions engine can express and one it cannot, and
// two gift cards. Everything comes from history.json beside this script, which
// the round trip reads too. Run from the repository root, after seed.mjs and
// stock.mjs:
//   node tools/shopify-seed/history.mjs
//
// A run can be repeated: a customer is found by their email, an order by its
// source identifier (tendero-seed-<key>), a discount by its code and a gift
// card by its note, and what is already there is left alone. Orders go
// through orderCreate, which a development store allows: each is dated by
// processedAt, the day it was placed, because the day it is created is the
// day of the seeding; it bypasses the stock, so the stock round trip still
// reads the seed's; and no receipt is sent. A gift card's code is shown by
// the store only when it is made, and is never printed here.
//
// The app needs write_customers, write_orders, write_discounts and
// write_gift_cards, and the protected customer data (name, email, phone,
// address) granted in the Dev Dashboard.
import fs from 'node:fs';
import { connect } from './shopify.mjs';

const history = JSON.parse(fs.readFileSync('tools/shopify-seed/history.json', 'utf8'));
const locations = JSON.parse(fs.readFileSync('tools/shopify-seed/locations.json', 'utf8'));
const { graphql } = await connect();

const CURRENCY = 'EUR';
/** Spain's standard rate: the seed's prices include it (ADR 0028). */
const VAT = { title: 'IVA', rate: 0.21 };
const TAG = 'tendero-seed';
/** What finishing an order needs to know of it, whether this run made it or an earlier one. */
const ORDER_FIELDS = 'id name sourceIdentifier cancelledAt refunds { id } transactions { id kind } lineItems(first: 50) { nodes { id sku } }';

const madrid = Object.entries(locations).find(([, code]) => code === 'MAD')?.[0];
if (!madrid) throw new Error('locations.json has no MAD location: run stock.mjs first.');

// ---- Customers, each with their address book; the first address is the default.
const customerOf = {};
for (const customer of history.customers) {
  const found = await graphql(`query ($q: String!) { customers(first: 1, query: $q) { nodes { id } } }`, {
    q: `email:"${customer.email}"`,
  });
  let id = found.customers.nodes[0]?.id;

  if (!id) {
    const answer = await graphql(
      `
        mutation ($input: CustomerInput!) {
          customerCreate(input: $input) {
            customer {
              id
            }
            userErrors {
              field
              message
            }
          }
        }
      `,
      {
        input: {
          firstName: customer.firstName,
          lastName: customer.lastName,
          email: customer.email,
          locale: customer.locale,
          tags: [TAG],
          emailMarketingConsent: {
            marketingState: customer.marketing,
            marketingOptInLevel: 'SINGLE_OPT_IN',
            consentUpdatedAt: '2025-10-01T00:00:00Z',
          },
        },
      },
    );
    refuseOn(answer.customerCreate.userErrors, customer.email);
    id = answer.customerCreate.customer.id;

    for (const [index, address] of customer.addresses.entries()) {
      const added = await graphql(
        `
          mutation ($customerId: ID!, $address: MailingAddressInput!, $setAsDefault: Boolean) {
            customerAddressCreate(customerId: $customerId, address: $address, setAsDefault: $setAsDefault) {
              userErrors {
                field
                message
              }
            }
          }
        `,
        { customerId: id, address: { firstName: customer.firstName, lastName: customer.lastName, ...address }, setAsDefault: index === 0 },
      );
      refuseOn(added.customerAddressCreate.userErrors, `${customer.email} address ${index + 1}`);
    }
    console.log(`customer  ${customer.key} added, ${customer.addresses.length} addresses`);
  } else {
    console.log(`customer  ${customer.key} already there`);
  }

  customerOf[customer.key] = { ...customer, id };
}

// ---- Discounts: a code discount each, by its code.
for (const discount of history.discounts) {
  const found = await graphql(`query ($code: String!) { codeDiscountNodeByCode(code: $code) { id } }`, { code: discount.code });
  if (found.codeDiscountNodeByCode) {
    console.log(`discount  ${discount.code} already there`);
    continue;
  }

  let items = { all: true };
  if (discount.collection) {
    const collection = await graphql(`query ($q: String!) { collections(first: 1, query: $q) { nodes { id } } }`, {
      q: `handle:${discount.collection}`,
    });
    const id = collection.collections.nodes[0]?.id;
    if (!id) throw new Error(`${discount.code}: the store has no collection ${discount.collection}; run collections.mjs first.`);
    items = { collections: { add: [id] } };
  }

  const everything = discount.combinesWithEverything === true;
  const answer = await graphql(
    `
      mutation ($input: DiscountCodeBasicInput!) {
        discountCodeBasicCreate(basicCodeDiscount: $input) {
          userErrors {
            field
            message
          }
        }
      }
    `,
    {
      input: {
        title: discount.title,
        code: discount.code,
        startsAt: discount.startsAt,
        usageLimit: discount.usageLimit ?? null,
        appliesOncePerCustomer: discount.appliesOncePerCustomer ?? false,
        combinesWith: { orderDiscounts: everything, productDiscounts: everything, shippingDiscounts: everything },
        context: { all: 'ALL' },
        customerGets: { value: { percentage: discount.percentage / 100 }, items },
      },
    },
  );
  refuseOn(answer.discountCodeBasicCreate.userErrors, discount.code);
  console.log(`discount  ${discount.code} added`);
}

// ---- Orders, each placed on its own day.
const variants = await variantsBySku();
const seeded = new Map();
for await (const order of taggedOrders()) seeded.set(order.sourceIdentifier, order);

for (const order of history.orders) {
  const source = `${TAG}-${order.key}`;
  if (seeded.has(source)) console.log(`order     ${order.key} already there`);
  await finish(order, seeded.get(source) ?? (await createOrder(order, source)));
}

/** The order as it was placed, paid unless it was cancelled, and delivered when it was fulfilled. */
async function createOrder(order, source) {
  const customer = customerOf[order.customer];
  const named = (address) => address && { firstName: customer.firstName, lastName: customer.lastName, ...address };
  const shippingAddress = named(order.shippingAddress ?? customer.addresses[0]);
  const billingAddress = named(order.billingAddress) ?? shippingAddress;

  const lines = order.lines.map((line) => {
    const variant = variants.get(line.sku);
    if (!variant) throw new Error(`order ${order.key}: the store has no variant with SKU ${line.sku}.`);
    return { ...line, variant };
  });
  const subtotal = cents(lines.reduce((sum, line) => sum + toCents(line.variant.price) * line.quantity, 0));
  // Taken off each line and added up, as the store allocates a percentage.
  const discount = order.discount
    ? cents(lines.reduce((sum, line) => sum + Math.round((toCents(line.variant.price) * line.quantity * order.discount.percentage) / 100), 0))
    : '0.00';
  const total = cents(toCents(subtotal) - toCents(discount) + toCents(order.shipping.amount));
  const tax = cents(Math.round((toCents(total) * VAT.rate) / (1 + VAT.rate)));
  const paid = order.state !== 'cancelled';

  const answer = await withOrderLimit(() =>
    graphql(
      `
        mutation ($order: OrderCreateOrderInput!, $options: OrderCreateOptionsInput) {
          orderCreate(order: $order, options: $options) {
            order {
              ${ORDER_FIELDS}
            }
            userErrors {
              field
              message
            }
          }
        }
      `,
      {
        order: {
          currency: CURRENCY,
          processedAt: order.placedAt,
          sourceIdentifier: source,
          tags: [TAG],
          customer: { toAssociate: { id: customer.id } },
          email: customer.email,
          shippingAddress,
          billingAddress,
          taxesIncluded: true,
          lineItems: lines.map((line) => ({
            variantId: line.variant.id,
            quantity: line.quantity,
            priceSet: money(line.variant.price),
          })),
          shippingLines: [{ title: order.shipping.title, priceSet: money(order.shipping.amount) }],
          taxLines: [{ ...VAT, priceSet: money(tax) }],
          discountCode: order.discount
            ? { itemPercentageDiscountCode: { code: order.discount.code, percentage: order.discount.percentage } }
            : null,
          financialStatus: paid ? 'PAID' : 'PENDING',
          transactions: paid
            ? [{ kind: 'SALE', status: 'SUCCESS', gateway: 'manual', amountSet: money(total), processedAt: order.placedAt }]
            : [],
          ...(order.state === 'fulfilled'
            ? { fulfillmentStatus: 'FULFILLED', fulfillment: { locationId: madrid, notifyCustomer: false, shipmentStatus: 'DELIVERED' } }
            : {}),
        },
        options: { inventoryBehaviour: 'BYPASS', sendReceipt: false, sendFulfillmentReceipt: false },
      },
    ),
  );
  refuseOn(answer.orderCreate.userErrors, `order ${order.key}`);
  console.log(`order     ${order.key} added as ${answer.orderCreate.order.name}, ${order.state}, ${total} ${CURRENCY}`);
  return answer.orderCreate.order;
}

/**
 * What follows an order's placing, each step done only when it is missing, so a
 * run that stopped half way finishes it: the cancellation, then the refund. The
 * refund's idempotency key is the order's own, so a step sent twice is one step.
 */
async function finish(order, created) {
  if (order.state === 'cancelled' && !created.cancelledAt) {
    const cancelled = await graphql(
      `
        mutation ($orderId: ID!) {
          orderCancel(orderId: $orderId, restock: false, reason: CUSTOMER, notifyCustomer: false) {
            orderCancelUserErrors {
              field
              message
            }
          }
        }
      `,
      { orderId: created.id },
    );
    refuseOn(cancelled.orderCancel.orderCancelUserErrors, `order ${order.key} cancel`);
    console.log(`order     ${order.key} cancelled`);
  }

  if (order.refund && created.refunds.length === 0) {
    const sale = created.transactions.find((transaction) => transaction.kind === 'SALE');
    const refunded = await graphql(
      `
        mutation ($input: RefundInput!, $key: String!) {
          refundCreate(input: $input) @idempotent(key: $key) {
            userErrors {
              field
              message
            }
          }
        }
      `,
      {
        input: {
          orderId: created.id,
          processedAt: order.refund.processedAt,
          notify: false,
          refundLineItems: order.refund.lines.map((line) => ({
            lineItemId: created.lineItems.nodes.find((each) => each.sku === line.sku).id,
            quantity: line.quantity,
            restockType: 'NO_RESTOCK',
          })),
          transactions: [{ orderId: created.id, parentId: sale.id, kind: 'REFUND', gateway: 'manual', amount: order.refund.amount }],
        },
        key: `${TAG}-${order.key}-refund`,
      },
    );
    refuseOn(refunded.refundCreate.userErrors, `order ${order.key} refund`);
    console.log(`order     ${order.key} refunded ${order.refund.amount} ${CURRENCY}`);
  }
}

// ---- Gift cards, by their note; the code the store answers is not printed.
const notes = new Set();
for await (const card of giftCards()) if (card.note) notes.add(card.note);

for (const card of history.giftCards) {
  const note = `${TAG}:${card.key}`;
  if (notes.has(note)) {
    console.log(`gift card ${card.key} already there`);
    continue;
  }

  const answer = await graphql(
    `
      mutation ($input: GiftCardCreateInput!) {
        giftCardCreate(input: $input) {
          userErrors {
            field
            message
          }
        }
      }
    `,
    {
      input: {
        initialAmount: { amount: card.amount, currencyCode: CURRENCY },
        customerId: card.customer ? customerOf[card.customer].id : null,
        expiresOn: card.expiresOn ?? null,
        note,
      },
    },
  );
  refuseOn(answer.giftCardCreate.userErrors, card.key);
  console.log(`gift card ${card.key} added, ${card.amount} ${CURRENCY}`);
}

/** Every variant by its SKU, with the price the store sells it at. */
async function variantsBySku() {
  const bySku = new Map();
  let after = null;
  do {
    const page = await graphql(
      `
        query ($after: String) {
          productVariants(first: 250, after: $after) {
            pageInfo {
              hasNextPage
              endCursor
            }
            nodes {
              id
              sku
              price
            }
          }
        }
      `,
      { after },
    );
    for (const variant of page.productVariants.nodes) bySku.set(variant.sku, variant);
    after = page.productVariants.pageInfo.hasNextPage ? page.productVariants.pageInfo.endCursor : null;
  } while (after);
  return bySku;
}

/** The orders an earlier run made, by their tag. */
async function* taggedOrders() {
  let after = null;
  do {
    const page = await graphql(
      `
        query ($after: String, $q: String!) {
          orders(first: 100, after: $after, query: $q) {
            pageInfo {
              hasNextPage
              endCursor
            }
            nodes {
              ${ORDER_FIELDS}
            }
          }
        }
      `,
      { after, q: `tag:${TAG}` },
    );
    yield* page.orders.nodes;
    after = page.orders.pageInfo.hasNextPage ? page.orders.pageInfo.endCursor : null;
  } while (after);
}

async function* giftCards() {
  let after = null;
  do {
    const page = await graphql(
      `
        query ($after: String) {
          giftCards(first: 100, after: $after) {
            pageInfo {
              hasNextPage
              endCursor
            }
            nodes {
              note
            }
          }
        }
      `,
      { after },
    );
    yield* page.giftCards.nodes;
    after = page.giftCards.pageInfo.hasNextPage ? page.giftCards.pageInfo.endCursor : null;
  } while (after);
}

/** A development store takes a few orders a minute through the API; past that, wait a minute and try again. */
async function withOrderLimit(send) {
  for (let attempt = 1; ; attempt++) {
    try {
      const answer = await send();
      const limited = answer.orderCreate.userErrors.some((error) => /limit|too many/i.test(error.message));
      if (!limited || attempt === 5) return answer;
    } catch (error) {
      if (!/limit|too many/i.test(error.message) || attempt === 5) throw error;
    }
    console.log('          the store asks to wait; trying again in a minute');
    await new Promise((resolve) => setTimeout(resolve, 60_000));
  }
}

function money(amount) {
  return { shopMoney: { amount, currencyCode: CURRENCY } };
}

function toCents(amount) {
  return Math.round(Number(amount) * 100);
}

function cents(value) {
  return (value / 100).toFixed(2);
}

function refuseOn(errors, about) {
  if (errors?.length) throw new Error(`${about} ${JSON.stringify(errors)}`);
}
