import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import type { ScreenContext, StewardAnswer, StewardTurnRequest, TurnMessage } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { type ConversationExchange, withConversation } from '@tendero/shared-util';
import { catchError, EMPTY, pipe, switchMap, takeUntil, tap } from 'rxjs';
import { StewardService } from '../../data-access/steward.service';
import { WaitingStore } from '../../shell/waiting.store';
import { BackofficeUsage } from '../../usage';

/** Where a question stands: on its way, answered, stopped by the person, or lost on the way. */
export type ExchangeState = 'asking' | 'answered' | 'stopped' | 'failed' | 'tooMany';

/** A question and what came back. The answer may say no model answered: that is an answer too. */
export interface Exchange extends ConversationExchange {
  readonly state: ExchangeState;
  readonly answer: StewardAnswer | null;
}

/** What the conversation has cost so far: the cost line under the panel's answers. */
export interface ConversationCost {
  readonly calls: number;
  readonly tokens: number;
  readonly microEuros: number;
  readonly milliseconds: number;
  /** The model that answered last. */
  readonly model: string | null;
}

/** Statuses after which this conversation cannot go on: only a new one does — or tomorrow, for the day's budget. */
const Ending: readonly StewardAnswer['status'][] = ['BudgetExceeded', 'ConversationTooLong', 'DailyBudgetExceeded'];

/**
 * Steward's conversation (phase 12; ADR 0047: a root store, so closing the panel
 * does not end the conversation — reopening it finds it). The conversation lives here
 * and travels with every turn — the server keeps none (spec, decision 6) — and a
 * page reload forgets it. A turn can be stopped: the request is aborted, which
 * stops the model's work on the server, and the panel can be asked again. That
 * much is `withConversation`'s, shared with Tino's; what a turn sends and what
 * an answer means are Steward's, here.
 */
export const StewardStore = signalStore(
  { providedIn: 'root' },
  withConversation<Exchange>(),
  withProps(() => ({
    _steward: inject(StewardService),
    _culture: inject(CultureStore),
    _waiting: inject(WaitingStore),
    _usage: inject(BackofficeUsage),
  })),
  withComputed((store) => {
    const last = store._last;
    return {
      /** Everything said so far, as the next turn sends it: questions and the answers that had words. */
      _history: computed<TurnMessage[]>(() =>
        store.exchanges().flatMap((exchange) =>
          exchange.state === 'answered' && exchange.answer?.text
            ? [
                { role: 'user', text: exchange.question },
                { role: 'assistant', text: exchange.answer.text },
              ]
            : [],
        ),
      ),
      cost: computed<ConversationCost>(() => {
        const costs = store.exchanges().flatMap((exchange) => (exchange.answer?.cost ? [exchange.answer.cost] : []));
        return {
          calls: costs.reduce((sum, cost) => sum + cost.calls, 0),
          tokens: costs.reduce((sum, cost) => sum + cost.inputTokens + cost.outputTokens, 0),
          microEuros: costs.reduce((sum, cost) => sum + cost.microEuros, 0),
          milliseconds: costs.reduce((sum, cost) => sum + cost.milliseconds, 0),
          model: costs[costs.length - 1]?.model ?? null,
        };
      }),
      ended: computed(() => {
        const status = last()?.answer?.status;
        return !!status && Ending.includes(status);
      }),
      nearLimit: computed(() => !!last()?.answer?.nearContextLimit),
      /** The newest answer's words, for the panel's one live region; empty while none. */
      latestAnswer: computed(() => (last()?.state === 'answered' ? (last()?.answer?.text ?? '') : '')),
    };
  }),
  withMethods((store) => {
    const settle = store._settle;

    /** One turn at a time: a new question never overtakes one on its way (the panel waits for it). */
    const send = rxMethod<{ id: number; request: StewardTurnRequest }>(
      pipe(
        switchMap(({ id, request }) =>
          store._steward.turn(request, store._culture.active()).pipe(
            takeUntil(store._stop),
            tap((answer) => {
              settle(id, { state: 'answered', answer });
              store._usage.stewardAnswered(answer);
              patchState(store, { conversationId: answer.conversationId });
              // The rail's badge counts what waits: these just started waiting.
              if (answer.proposalIds.length > 0) store._waiting.refresh();
            }),
            catchError((failure: unknown) => {
              const tooMany = failure instanceof HttpErrorResponse && failure.status === 429;
              settle(id, { state: tooMany ? 'tooMany' : 'failed' });
              return EMPTY;
            }),
          ),
        ),
      ),
    );

    return {
      ask(question: string, screen: ScreenContext | null): void {
        const text = question.trim();
        if (!text || store.busy() || store.ended()) return;

        const id = store._begin(text, { answer: null });
        const request: StewardTurnRequest = {
          messages: [...store._history(), { role: 'user', text }],
          screen,
          conversationId: store.conversationId(),
        };
        send({ id, request });
      },
    };
  }),
);
