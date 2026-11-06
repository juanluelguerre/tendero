import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import type { SearchHit, TinoAnswer, TinoContext, TinoReason, TinoTurnRequest, TurnMessage } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { type ConversationExchange, withConversation } from '@tendero/shared-util';
import { catchError, map, type Observable, of, pipe, switchMap, takeUntil, tap } from 'rxjs';
import { ProductSearchService } from '../data-access/product-search.service';
import { TinoService } from '../data-access/tino.service';

/** Where a question stands: on its way, answered by Tino, answered by the search instead, or stopped by the shopper. */
export type ExchangeState = 'asking' | 'answered' | 'fallback' | 'stopped';

/** Why the search answered instead of Tino, as the tracking plan names it. */
export type FallbackCause = TrackedEvents['tino_unavailable']['cause'];

/**
 * A question and what came back. `cards` are the shop's own: for an answer, the
 * products it names, read by their codes; for the fallback, what the search found
 * for the shopper's words. Never anything a model wrote.
 */
export interface Exchange extends ConversationExchange {
  readonly state: ExchangeState;
  readonly answer: TinoAnswer | null;
  readonly cards: readonly SearchHit[];
  /** For the fallback: how many the search found in all. */
  readonly found: number;
  /** For the fallback: nothing matched the words, so the search found only the nearest (its answer is `Related`). */
  readonly nearest: boolean;
  readonly cause: FallbackCause | null;
}

/** How many of the search's results the fallback shows in the panel; the rest are a link away. */
export const FallbackCards = 4;

const Causes: Record<NonNullable<TinoAnswer['cause']>, FallbackCause> = {
  NoProfile: 'no_profile',
  Model: 'model',
  Time: 'time',
  Budget: 'budget',
  Shop: 'shop',
  TooLong: 'too_long',
  Scorer: 'scorer',
};

const Kinds: Record<NonNullable<TinoAnswer['kind']>, TrackedEvents['tino_answered']['kind']> = {
  Products: 'products',
  Shop: 'shop',
  Ask: 'ask',
  NoData: 'no_data',
  Order: 'order',
};

/** Every sentence of an answer the shopper is shown, each with the fact under it. */
export function reasonsOf(answer: TinoAnswer): readonly TinoReason[] {
  return [...answer.products.flatMap((product) => product.reasons), ...answer.says, ...(answer.noData?.nearest ?? [])];
}

/** An answer as the words it had, for the next turn's history: the server keeps no conversation. */
function wordsOf(answer: TinoAnswer): string {
  return [
    ...answer.products.map((product) => [product.name, ...product.reasons.map((reason) => reason.text)].join('. ')),
    ...answer.says.map((reason) => reason.text),
    answer.ask,
    answer.noData?.sentence,
  ]
    .filter(Boolean)
    .join('\n');
}

/**
 * Tino's conversation (phase 12b; ADR 0047: a root store, so closing the panel
 * does not end it). The conversation lives here and travels with every turn —
 * the server keeps none — and a page reload forgets it. That much is
 * `withConversation`'s, shared with Steward's; what follows is Tino's.
 *
 * **When Tino does not answer the search does** (invariant 8): whatever the
 * cause — no model, the turn's time, the day's budget, the request itself
 * failing — the shopper's own words are searched for and the results are the
 * answer, with a sentence that says so.
 */
export const TinoStore = signalStore(
  { providedIn: 'root' },
  withConversation<Exchange>(),
  withProps(() => ({
    _tino: inject(TinoService),
    _search: inject(ProductSearchService),
    _culture: inject(CultureStore),
    _telemetry: inject(Telemetry),
  })),
  withComputed((store) => {
    const last = store._last;
    return {
      /** Everything said so far, as the next turn sends it: questions and the answers that had words. */
      _history: computed<TurnMessage[]>(() =>
        store.exchanges().flatMap((exchange) => {
          const words = exchange.state === 'answered' && exchange.answer ? wordsOf(exchange.answer) : '';
          return words
            ? [
                { role: 'user', text: exchange.question },
                { role: 'assistant', text: words },
              ]
            : [];
        }),
      ),
      /** What a screen reader is told when an answer arrives: that there is one, not all of it again. */
      latest: computed(() => (last()?.state === 'asking' ? null : last())),
    };
  }),
  withMethods((store) => {
    const settle = store._settle;

    /** The search's results for the shopper's words: what there is when Tino is not. */
    const searched = (text: string): Observable<Pick<Exchange, 'cards' | 'found' | 'nearest'>> =>
      store._search.search(text, store._culture.active(), FallbackCards).pipe(
        map((page) => ({
          cards: page.hits.slice(0, FallbackCards),
          found: page.total,
          nearest: page.answer?.match === 'Related',
        })),
        catchError(() => of({ cards: [] as readonly SearchHit[], found: 0, nearest: false })),
      );

    const fellBack = (id: number, question: string, cause: FallbackCause, answer: TinoAnswer | null) => {
      store._telemetry.trackEvent('tino_unavailable', { cause });
      return searched(answer?.searchText || question).pipe(
        tap((results) => settle(id, { state: 'fallback', answer, cause, ...results })),
      );
    };

    /**
     * The cards of the products an answer names, in its order: the shop's read of
     * those codes. The product a "its details do not say" is about is one of them —
     * the sentence says "this product", and the conversation outlives the page.
     */
    const answered = (id: number, answer: TinoAnswer) => {
      const about = answer.noData?.code;
      const codes = [...answer.products.map((product) => product.code), ...(about ? [about] : [])];
      const cards$ =
        codes.length > 0
          ? store._search.cards(store._culture.active(), { codes }).pipe(
              map((result) => result.items),
              catchError(() => of([] as readonly SearchHit[])),
            )
          : of([] as readonly SearchHit[]);
      return cards$.pipe(
        tap((cards) => {
          settle(id, { state: 'answered', answer, cards });
          store._telemetry.trackEvent('tino_answered', {
            kind: Kinds[answer.kind ?? 'Ask'],
            products: answer.products.length,
            reasons: reasonsOf(answer).length,
            reasons_dropped: answer.reasonsDropped,
            model_calls: answer.cost?.calls ?? 0,
          });
        }),
      );
    };

    /** One turn at a time: a new question never overtakes one on its way. */
    const send = rxMethod<{ id: number; question: string; request: TinoTurnRequest }>(
      pipe(
        switchMap(({ id, question, request }) =>
          store._tino.turn(request, store._culture.active()).pipe(
            switchMap((answer) => {
              patchState(store, { conversationId: answer.conversationId });
              return answer.status === 'Answered'
                ? answered(id, answer)
                : fellBack(id, question, Causes[answer.cause ?? 'Model'], answer);
            }),
            catchError((failure: unknown) =>
              fellBack(
                id,
                question,
                failure instanceof HttpErrorResponse && failure.status === 429 ? 'too_many' : 'network',
                null,
              ),
            ),
            takeUntil(store._stop),
          ),
        ),
      ),
    );

    return {
      ask(question: string, context: TinoContext | null): void {
        const text = question.trim();
        if (!text || store.busy()) return;

        const id = store._begin(text, { answer: null, cards: [], found: 0, nearest: false, cause: null });
        const request: TinoTurnRequest = {
          messages: [...store._history(), { role: 'user', text }],
          context,
          conversationId: store.conversationId(),
        };
        send({ id, question: text, request });
      },

      /** The shopper starts over from the panel's head: the conversation is forgotten, and how long it was is reported. */
      restart(): void {
        store._telemetry.trackEvent('tino_conversation_restarted', { exchanges: store.exchanges().length });
        store.newConversation();
      },
    };
  }),
);
