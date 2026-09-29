import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import type { OrderView, ReturnView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { beforeEach, describe, expect, it } from 'vitest';
import { OrderService } from '../../data-access/order.service';
import { OrderStore } from './order.store';

const order = (status = 'Delivered'): OrderView =>
  ({ orderId: 'O-1', status, currency: 'EUR', lines: [{ sku: 'MUG', quantity: 3 }, { sku: 'TEA', quantity: 1 }] }) as unknown as OrderView;

/** The order page's own state (ADR 0047): the order, what can still go back, and the return being put together. */
describe('the order store', () => {
  let handedOver: OrderView | undefined;
  let reads: number;
  let returning: () => Promise<ReturnView>;
  let sent: unknown[];

  beforeEach(() => {
    handedOver = undefined;
    reads = 0;
    returning = async () => ({ returnId: 'R-1', lines: [] }) as unknown as ReturnView;
    sent = [];
  });

  function opened() {
    TestBed.configureTestingModule({
      providers: [
        OrderStore,
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'O-1' }) } } },
        { provide: Router, useValue: { getCurrentNavigation: () => (handedOver ? { extras: { state: { order: handedOver } } } : null) } },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        {
          provide: OrderService,
          useValue: {
            get: async () => (reads++, { order: order(), returns: sent.map(() => ({ returnId: 'R-1', lines: [] })), returnable: [{ sku: 'MUG', quantity: 2 }] }),
            requestReturn: async (_id: string, request: unknown) => (sent.push(request), returning()),
          },
        },
      ],
    });
    return TestBed.inject(OrderStore);
  }

  const settled = () => new Promise((done) => setTimeout(done));

  /** Straight from checkout: the order is on screen at once, and read again for what can go back. */
  it('shows the order checkout handed over at once, and reads it again', async () => {
    handedOver = order('Placed');
    const store = opened();
    const atOnce = [store.order()?.status, store.justPlaced(), store.loading()];
    await settled();

    expect([atOnce, reads, store.order()?.status]).toEqual([['Placed', true, false], 1, 'Delivered']);
  });

  it('offers back only what is left of each line, and keeps a quantity within it', async () => {
    const store = opened();
    await settled();
    const [mug] = store.returnableLines();
    store.toggle(mug);
    store.setQuantity('MUG', 9);

    expect([store.returnableLines().map((line) => line.sku), store.quantityOf('MUG')]).toEqual([['MUG'], 2]);
  });

  it('sends the lines picked, then clears them and says the return is open', async () => {
    const store = opened();
    await settled();
    store.toggle(store.returnableLines()[0]);
    await store.requestReturn();

    expect([sent, store.picked().size, store.returns().length, store.returnMessage(), store.returnOpened()]).toEqual([
      [{ lines: [{ sku: 'MUG', quantity: 1, reason: 'WrongSize', comment: null }] }],
      0,
      1,
      'order.returnOpened',
      true,
    ]);
  });

  /** The window closed between opening the page and asking: the shop says so, not "something went wrong". */
  it('says the return window has closed', async () => {
    returning = () => Promise.reject({ status: 409 });
    const store = opened();
    await settled();
    store.toggle(store.returnableLines()[0]);
    await store.requestReturn();

    expect([store.returnMessage(), store.returnOpened()]).toEqual(['order.returnClosed', false]);
  });
});
