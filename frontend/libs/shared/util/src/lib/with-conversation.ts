import { computed } from '@angular/core';
import { patchState, signalStoreFeature, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { Subject } from 'rxjs';

/** What every exchange with an agent has, whoever the agent is: a question, and where it stands. */
export interface ConversationExchange {
  readonly id: number;
  readonly question: string;
  /** `asking` while the turn is out and `stopped` when the person stopped it; the rest is each agent's. */
  readonly state: string;
}

let exchanges = 0;

/**
 * A conversation with an agent, as the state and behaviour its store needs
 * whichever agent it is — Steward's in the backoffice, Tino's in the storefront
 * (ADR 0047: a feature for what more than one store needs). The conversation
 * lives in the store and travels with every turn, because the server keeps
 * none; one turn is out at a time; a turn can be stopped; and a new
 * conversation forgets the old one.
 *
 * What a turn sends, what an answer is and what failing means are each agent's:
 * its store begins an exchange (`_begin`), sends its own request until `_stop`,
 * and settles the exchange with what came back (`_settle`).
 */
export function withConversation<TExchange extends ConversationExchange>() {
  return signalStoreFeature(
    withState({
      conversationId: null as string | null,
      exchanges: [] as readonly TExchange[],
    }),
    withProps(() => ({
      /** Fires when the turn on its way is to be dropped: a store's request runs until it does. */
      _stop: new Subject<void>(),
    })),
    withComputed((store) => {
      const last = computed(() => store.exchanges()[store.exchanges().length - 1] ?? null);
      return {
        _last: last,
        busy: computed(() => last()?.state === 'asking'),
      };
    }),
    withMethods((store) => {
      const settle = (id: number, change: Partial<TExchange>): void =>
        patchState(store, (state) => ({
          exchanges: state.exchanges.map((exchange) => (exchange.id === id ? { ...exchange, ...change } : exchange)),
        }));

      return {
        /** A question on its way, with whatever else an exchange of this agent starts with; its id. */
        _begin(question: string, blank: Omit<TExchange, keyof ConversationExchange>): number {
          const id = ++exchanges;
          const exchange = { ...blank, id, question, state: 'asking' } as unknown as TExchange;
          patchState(store, (state) => ({ exchanges: [...state.exchanges, exchange] }));
          return id;
        },
        _settle: settle,
        /** Stops the turn on its way: the request is aborted, and the model's work on the server with it. */
        stop(): void {
          const asking = store.exchanges().find((exchange) => exchange.state === 'asking');
          store._stop.next();
          if (asking) settle(asking.id, { state: 'stopped' } as Partial<TExchange>);
        },
        newConversation(): void {
          store._stop.next();
          patchState(store, { conversationId: null, exchanges: [] as readonly TExchange[] });
        },
      };
    }),
  );
}
