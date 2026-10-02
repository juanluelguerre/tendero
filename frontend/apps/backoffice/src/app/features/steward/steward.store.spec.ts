import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { StewardAnswer } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { StewardStore } from './steward.store';

const Turns = 'http://api/api/steward/turns';

const answer = (text: string | null, extra: Partial<StewardAnswer> = {}): StewardAnswer => ({
  status: 'Answered',
  text,
  reads: [],
  proposalIds: [],
  cost: { calls: 2, inputTokens: 1000, outputTokens: 100, microEuros: 2100, milliseconds: 4100, model: 'qwen' },
  profile: 'local',
  fellBack: false,
  nearContextLimit: false,
  conversationId: 'conv-1',
  ...extra,
});

/**
 * Steward's conversation (ADR 0047: a root store, so it outlives the panel being
 * closed). The conversation travels with every turn — the server keeps none —
 * and a turn can be stopped.
 */
describe('the steward store', () => {
  let http: HttpTestingController;
  let store: InstanceType<typeof StewardStore>;
  const screen = { page: 'Products', subject: 'Active' };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(StewardStore);
  });

  afterEach(() => http.match(() => true));

  /** The first turn has no conversation; the next carries the one the server minted, and everything said so far. */
  it('sends the conversation so far, and the id the first answer brought', () => {
    store.ask('Which products have no English name?', screen);
    const first = http.expectOne((req) => req.url === Turns);
    expect(first.request.body).toEqual({
      messages: [{ role: 'user', text: 'Which products have no English name?' }],
      screen,
      conversationId: null,
    });
    first.flush(answer('Twelve.'));

    store.ask('Propose them.', screen);
    const second = http.expectOne((req) => req.url === Turns);

    expect(second.request.body.conversationId).toBe('conv-1');
    expect(second.request.body.messages).toEqual([
      { role: 'user', text: 'Which products have no English name?' },
      { role: 'assistant', text: 'Twelve.' },
      { role: 'user', text: 'Propose them.' },
    ]);
  });

  /** Stop aborts the request — the server stops the model with it — and the panel can be asked again. */
  it('stops a turn: the request is cancelled and nothing is answered', () => {
    store.ask('Slow question', screen);
    const request = http.expectOne((req) => req.url === Turns);
    store.stop();

    expect([request.cancelled, store.busy(), store.exchanges()[0].state]).toEqual([true, false, 'stopped']);
  });

  it('keeps what a turn that answered nothing says, and leaves it out of the history', () => {
    store.ask('First', screen);
    http.expectOne((req) => req.url === Turns).flush(answer(null, { status: 'Unavailable', cost: null }));
    store.ask('Second', screen);

    expect(store.exchanges()[0].answer?.status).toBe('Unavailable');
    expect(http.expectOne((req) => req.url === Turns).request.body.messages).toEqual([
      { role: 'user', text: 'Second' },
    ]);
  });

  it('tells too many questions apart from a failure', () => {
    store.ask('One', screen);
    http.expectOne((req) => req.url === Turns).flush('', { status: 429, statusText: 'Too Many Requests' });
    store.ask('Two', screen);
    http.expectOne((req) => req.url === Turns).flush('', { status: 500, statusText: 'Error' });

    expect(store.exchanges().map((exchange) => exchange.state)).toEqual(['tooMany', 'failed']);
  });

  /** The cost line is the conversation's: every turn's calls, tokens, cost and time added up. */
  it('adds up what the conversation cost', () => {
    store.ask('One', screen);
    http.expectOne((req) => req.url === Turns).flush(answer('A'));
    store.ask('Two', screen);
    http.expectOne((req) => req.url === Turns).flush(answer('B'));

    expect(store.cost()).toEqual({ calls: 4, tokens: 2200, microEuros: 4200, milliseconds: 8200, model: 'qwen' });
  });

  /** Proposals arrived: the rail's badge reads its count again, not only after the next navigation. */
  it('asks the badges again when an answer brings proposals', () => {
    const badge = (req: { url: string; params: { get(name: string): string | null } }) =>
      req.url === 'http://api/api/steward/proposals' && req.params.get('filter') === 'ToReview';
    // The badges' first reading, answered: what follows is what this answer asks for.
    TestBed.tick();
    http.match(badge).forEach((req) => req.flush({ counts: [] }));
    store.ask('One', screen);
    http.expectOne((req) => req.url === Turns).flush(answer('A', { proposalIds: ['p1'] }));
    TestBed.tick();

    expect(http.match(badge)).toHaveLength(1);
  });

  /** Over the budget or past every window, the conversation is over: only a new one goes on. */
  it('ends a conversation that is too long, and starts a new one from nothing', () => {
    store.ask('One', screen);
    http.expectOne((req) => req.url === Turns).flush(answer(null, { status: 'ConversationTooLong', cost: null }));
    const ended = store.ended();
    store.newConversation();
    store.ask('Again', screen);

    expect([ended, store.ended(), store.exchanges().length]).toEqual([true, false, 1]);
    expect(http.expectOne((req) => req.url === Turns).request.body.conversationId).toBeNull();
  });

  it('says when the conversation nears the model’s window', () => {
    store.ask('One', screen);
    http.expectOne((req) => req.url === Turns).flush(answer('A', { nearContextLimit: true }));

    expect(store.nearLimit()).toBe(true);
  });
});
