import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { CartStore } from '../../cart/cart.store';
import { OrderService } from '../../data-access/order.service';
import { ContinueCheckoutStore } from './continue-checkout.store';

/** An agent's checkout a person finishes here (P11-1; ADR 0047): the cart taken over, then checkout in the agent's language. */
describe('the continue-checkout store', () => {
  let handOff: () => Promise<{ cartToken: string; culture: string }>;
  let adopted: string[];
  let went: [unknown[], unknown][];

  beforeEach(() => {
    handOff = async () => ({ cartToken: 'T-9', culture: 'en' });
    adopted = [];
    went = [];
  });

  async function opened() {
    TestBed.configureTestingModule({
      providers: [
        ContinueCheckoutStore,
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ sessionId: 'S-1' }) } } },
        { provide: OrderService, useValue: { handOff: () => handOff() } },
        { provide: CartStore, useValue: { adopt: async (token: string) => void adopted.push(token) } },
        { provide: Router, useValue: { navigate: async (commands: unknown[], extras: unknown) => void went.push([commands, extras]) } },
      ],
    });
    const store = TestBed.inject(ContinueCheckoutStore);
    await store.takeOver();
    return store;
  }

  it('takes the cart over and goes on to checkout, replacing this address', async () => {
    const store = await opened();

    expect([adopted, went, store.state()]).toEqual([['T-9'], [[['/', 'en', 'checkout'], { replaceUrl: true }]], 'taking-over']);
  });

  it('says a hand-off that no longer exists is gone, and anything else failed', async () => {
    handOff = () => Promise.reject({ status: 404 });
    const gone = (await opened()).state();
    TestBed.resetTestingModule();
    handOff = () => Promise.reject({ status: 503 });

    expect([gone, (await opened()).state()]).toEqual(['gone', 'failed']);
  });
});
