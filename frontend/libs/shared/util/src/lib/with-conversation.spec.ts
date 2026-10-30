// @vitest-environment jsdom
import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { setupTestBed } from '@analogjs/vitest-angular/setup-testbed';
import { signalStore, withMethods } from '@ngrx/signals';
import { describe, expect, it } from 'vitest';
import { type ConversationExchange, withConversation } from './with-conversation';

setupTestBed();

interface Exchange extends ConversationExchange {
  readonly state: 'asking' | 'answered' | 'stopped';
  readonly answer: string | null;
}

/** An agent's store, as small as one can be: it begins, and settles with what it is told. */
const Store = signalStore(
  withConversation<Exchange>(),
  withMethods((store) => ({
    ask: (question: string) => store._begin(question, { answer: null }),
    answer: (id: number, answer: string) => store._settle(id, { state: 'answered', answer }),
    stopped: () => store._stop,
  })),
);

/** What Steward's and Tino's conversations share (phase 12b). */
describe('a conversation', () => {
  const made = () => {
    TestBed.configureTestingModule({ providers: [Store] });
    return TestBed.inject(Store);
  };

  it('is busy while a question is on its way, and not once it is settled', () => {
    const store = made();
    expect(store.busy()).toBe(false);

    const id = store.ask('una sartén');
    expect([store.busy(), store.exchanges()]).toEqual([
      true,
      [{ id, question: 'una sartén', state: 'asking', answer: null }],
    ]);

    store.answer(id, 'esta');
    expect([store.busy(), store.exchanges()[0].state, store.exchanges()[0].answer]).toEqual([
      false,
      'answered',
      'esta',
    ]);
  });

  it('gives every exchange an id of its own and settles only the one named', () => {
    const store = made();
    const first = store.ask('una');
    store.answer(first, 'a');
    const second = store.ask('otra');

    expect(second).not.toBe(first);
    expect(store.exchanges().map((exchange) => exchange.state)).toEqual(['answered', 'asking']);

    store.answer(second, 'b');
    expect(store.exchanges().map((exchange) => exchange.answer)).toEqual(['a', 'b']);
  });

  it('stops only the turn on its way, never one already answered', () => {
    const store = made();
    store.answer(store.ask('una'), 'a');
    store.ask('otra');

    store.stop();

    expect(store.exchanges().map((exchange) => exchange.state)).toEqual(['answered', 'stopped']);
  });

  it('stops with nothing on its way and leaves the conversation as it was', () => {
    const store = made();
    let told = 0;
    store.stopped().subscribe(() => told++);
    store.answer(store.ask('una'), 'a');

    store.stop();

    expect([told, store.exchanges().map((exchange) => exchange.state)]).toEqual([1, ['answered']]);
  });

  it('stops the turn on its way: whoever sent it is told, and the exchange says stopped', () => {
    const store = made();
    let told = 0;
    store.stopped().subscribe(() => told++);
    store.ask('una sartén');

    store.stop();

    expect([told, store.busy(), store.exchanges()[0].state]).toEqual([1, false, 'stopped']);
  });

  it('forgets everything for a new conversation, and drops a turn on its way', () => {
    const store = made();
    let told = 0;
    store.stopped().subscribe(() => told++);
    store.ask('una sartén');

    store.newConversation();

    expect([told, store.conversationId(), store.exchanges()]).toEqual([1, null, []]);
  });
});
