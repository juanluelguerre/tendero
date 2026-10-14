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
  ({
    orderId: 'O-1',
    status,
    lines: [
      { sku: 'MUG', quantity: 3 },
      { sku: 'TEA', quantity: 1 },
    ],
  }) as unknown as OrderView;

/** The order page's own state (ADR 0047): the order, what can still go back, and the return being put together. */
describe('the order store', () => {
  let handedOver: OrderView | undefined;
  let reads: number;
  /** The languages the order was read in. */
  let readIn: string[];
  let culture: ReturnType<typeof signal<string>>;
  let returning: () => Promise<ReturnView>;
  let sent: unknown[];

  beforeEach(() => {
    handedOver = undefined;
    reads = 0;
    readIn = [];
    culture = signal('es');
    returning = async () => ({ returnId: 'R-1', lines: [] }) as unknown as ReturnView;
    sent = [];
  });

  function opened() {
    TestBed.configureTestingModule({
      providers: [
        OrderStore,
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'O-1' }) } } },
        {
          provide: Router,
          useValue: { getCurrentNavigation: () => (handedOver ? { extras: { state: { order: handedOver } } } : null) },
        },
        { provide: CultureStore, useValue: { active: culture } },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        {
          provide: OrderService,
          useValue: {
            get: async (_id: string, language: string) => (
              reads++,
              readIn.push(language),
              {
                order: order(),
                returns: sent.map(() => ({ returnId: 'R-1', lines: [] })),
                returnable: [{ sku: 'MUG', quantity: 2 }],
              }
            ),
            requestReturn: async (_id: string, request: unknown) => (sent.push(request), returning()),
          },
        },
      ],
    });
    return TestBed.inject(OrderStore);
  }

  const settled = () => new Promise((done) => setTimeout(done));

  /** Switching language keeps the page (one route for every language): the order is read again in the new one. */
  it('reads the order again in the new language when the language changes', async () => {
    opened();
    await settled();

    culture.set('en');
    TestBed.tick();
    await settled();

    expect(readIn).toEqual(['es', 'en']);
  });

  /** Straight from checkout: the order is on screen at once, and read again for what can go back. */
  it('shows the order checkout handed over at once, and reads it again', async () => {
    handedOver = order('Placed');
    const store = opened();
    const atOnce = [store.order()?.status, store.justPlaced(), store.loading()];
    await settled();

    expect([atOnce, reads, store.order()?.status]).toEqual([['Placed', true, false], 1, 'Delivered']);
  });

  /**
   * The return is a Signal Form: a line is picked by its checkbox, its quantity
   * typed into its field. More than is left is invalid and is not sent — it used
   * to be clamped in silence, which changed what the shopper had typed.
   */
  it('offers back only what is left of each line, and does not send more than that', async () => {
    const store = opened();
    await settled();
    store.returnForm[0].picked().value.set(true);
    store.returnForm[0].quantity().value.set(9);
    const tooMany = [store.returnForm().valid(), store.canSend()];
    store.returnForm[0].quantity().value.set(2);

    expect([store.returnableLines().map((line) => line.sku), tooMany, store.canSend()]).toEqual([
      ['MUG'],
      [false, false],
      true,
    ]);
  });

  it('sends the lines picked, then clears them and says the return is open', async () => {
    const store = opened();
    await settled();
    store.returnForm[0].picked().value.set(true);
    await store.requestReturn();

    expect([sent, store.pickedCount(), store.returns().length, store.returnMessage(), store.returnOpened()]).toEqual([
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
    store.returnForm[0].picked().value.set(true);
    await store.requestReturn();

    expect([store.returnMessage(), store.returnOpened()]).toEqual(['order.returnClosed', false]);
  });
});
