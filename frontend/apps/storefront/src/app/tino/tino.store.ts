import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import type { SearchHit, TinoAnswer, TinoContext, TinoReason, TinoTurnRequest, TurnMessage } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { catchError, map, type Observable, of, pipe, Subject, switchMap, takeUntil, tap } from 'rxjs';
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
export interface Exchange {
  readonly id: number;
  readonly question: string;
  readonly state: ExchangeState;
  readonly answer: TinoAnswer | null;
  readonly cards: readonly SearchHit[];
  /** For the fallback: how many the search found in all. */
  readonly found: number;
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

let exchanges = 0;

/**
 * Tino's conversation (phase 12b; ADR 0047: a root store, so closing the panel
 * does not end it). The conversation lives here and travels with every turn —
 * the server keeps none — and a page reload forgets it.
 *
 * **When Tino does not answer the search does** (invariant 8): whatever the
 * cause — no model, the turn's time, the day's budget, the request itself
 * failing — the shopper's own words are searched for and the results are the
 * answer, with a sentence that says so.
 */
export const TinoStore = signalStore(
  { providedIn: 'root' },
  withState({
    conversationId: null as string | null,
    exchanges: [] as readonly Exchange[],
  }),
  withProps(() => ({
    _tino: inject(TinoService),
    _search: inject(ProductSearchService),
    _culture: inject(CultureStore),
    _telemetry: inject(Telemetry),
    _stop: new Subject<void>(),
  })),
  withComputed((store) => {
    const last = computed(() => store.exchanges()[store.exchanges().length - 1] ?? null);
    return {
      busy: computed(() => last()?.state === 'asking'),
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
    const settle = (id: number, change: Partial<Exchange>) =>
      patchState(store, (state) => ({
        exchanges: state.exchanges.map((exchange) => (exchange.id === id ? { ...exchange, ...change } : exchange)),
      }));

    /** The search's results for the shopper's words: what there is when Tino is not. */
    const searched = (text: string): Observable<Pick<Exchange, 'cards' | 'found'>> =>
      store._search.search(text, store._culture.active(), FallbackCards).pipe(
        map((page) => ({ cards: page.hits.slice(0, FallbackCards), found: page.total })),
        catchError(() => of({ cards: [] as readonly SearchHit[], found: 0 })),
      );

    const fellBack = (id: number, question: string, cause: FallbackCause, answer: TinoAnswer | null) => {
      store._telemetry.trackEvent('tino_unavailable', { cause });
      return searched(answer?.searchText || question).pipe(
        tap((results) => settle(id, { state: 'fallback', answer, cause, ...results })),
      );
    };

    /** The cards of the products an answer names, in its order: the shop's read of those codes. */
    const answered = (id: number, answer: TinoAnswer) => {
      const codes = answer.products.map((product) => product.code);
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

        const id = ++exchanges;
        const request: TinoTurnRequest = {
          messages: [...store._history(), { role: 'user', text }],
          context,
          conversationId: store.conversationId(),
        };
        patchState(store, (state) => ({
          exchanges: [
            ...state.exchanges,
            { id, question: text, state: 'asking' as const, answer: null, cards: [], found: 0, cause: null },
          ],
        }));
        send({ id, question: text, request });
      },
      /** Stops the turn on its way: the request is aborted, and the model's work with it. */
      stop(): void {
        const asking = store.exchanges().find((exchange) => exchange.state === 'asking');
        store._stop.next();
        if (asking) settle(asking.id, { state: 'stopped' });
      },
      newConversation(): void {
        store._stop.next();
        patchState(store, { conversationId: null, exchanges: [] });
      },
    };
  }),
);
