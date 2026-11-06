import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { answer, hit, reason, unavailable } from './tino-fixtures';
import { TinoStore } from './tino.store';

const Turns = 'http://api/api/assistant/turns';
const Cards = 'http://api/api/search/cards';
const Search = 'http://api/api/search';

/**
 * Tino's conversation: it travels with every turn, an answer's products are the
 * shop's cards, and when Tino does not answer the search does (invariant 8).
 */
describe('the tino store', () => {
  let http: HttpTestingController;
  const telemetry = { trackEvent: vi.fn() };
  let store: InstanceType<typeof TinoStore>;

  beforeEach(() => {
    telemetry.trackEvent.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        { provide: Telemetry, useValue: telemetry },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(CultureStore).use('es');
    store = TestBed.inject(TinoStore);
  });

  afterEach(() => http.match(() => true));

  const turn = () => http.expectOne((req) => req.url === Turns);
  const last = () => store.exchanges()[store.exchanges().length - 1];

  it('sends the question with what the shopper is looking at, and then the conversation so far as words', () => {
    store.ask('  ¿puede ir al horno?  ', { kind: 'Product', key: 'PAN-1' });
    const first = turn();
    expect(first.request.body).toEqual({
      messages: [{ role: 'user', text: '¿puede ir al horno?' }],
      context: { kind: 'Product', key: 'PAN-1' },
      conversationId: null,
    });
    expect(first.request.params.get('culture')).toBe('es');
    first.flush(answer());
    http.expectOne((req) => req.url === Cards).flush({ items: [hit('PAN-1')] });

    store.ask('¿y cuánto pesa?', null);
    const second = turn();

    // An answer has no prose to send back: its products and their sentences are its words.
    expect(second.request.body).toEqual({
      messages: [
        { role: 'user', text: '¿puede ir al horno?' },
        { role: 'assistant', text: 'Sartén de hierro. Vale para inducción.' },
        { role: 'user', text: '¿y cuánto pesa?' },
      ],
      context: null,
      conversationId: 'conv-1',
    });
  });

  /** The card is the shop's read of the code the answer names: never a name or a price a model wrote. */
  it("draws an answer's products with the shop's own cards, asked for by their codes", () => {
    store.ask('una sartén', null);
    expect([store.busy(), last().state]).toEqual([true, 'asking']);
    turn().flush(
      answer({
        products: [
          { code: 'PAN-1', name: 'Sartén de hierro', reasons: [] },
          { code: 'PAN-2', name: 'Sartén de acero', reasons: [] },
        ],
      }),
    );

    const cards = http.expectOne((req) => req.url === Cards);
    expect([cards.request.params.getAll('codes'), cards.request.params.get('culture')]).toEqual([
      ['PAN-1', 'PAN-2'],
      'es',
    ]);
    expect(last().state).toBe('asking');
    cards.flush({ items: [hit('PAN-2', 'Sartén de acero'), hit('PAN-1')] });

    expect([store.busy(), last().state, last().cards.map((card) => card.code)]).toEqual([
      false,
      'answered',
      ['PAN-2', 'PAN-1'],
    ]);
  });

  it('answers without cards when there are no products, and still answers when the cards do not come', () => {
    store.ask('quiero algo', null);
    turn().flush(answer({ kind: 'Ask', products: [], ask: '¿Qué buscas?', reads: [] }));
    http.expectNone((req) => req.url === Cards);
    expect([last().state, last().answer?.ask]).toEqual(['answered', '¿Qué buscas?']);

    store.ask('una sartén', null);
    turn().flush(answer());
    http.expectOne((req) => req.url === Cards).flush('down', { status: 500, statusText: 'Server Error' });
    expect([last().state, last().cards]).toEqual(['answered', []]);
  });

  it('counts an answer by its kind and its numbers, and never by what was asked', () => {
    store.ask('una sartén para inducción', null);
    turn().flush(answer());
    http.expectOne((req) => req.url === Cards).flush({ items: [hit('PAN-1')] });

    expect(telemetry.trackEvent.mock.calls).toEqual([
      ['tino_answered', { kind: 'products', products: 1, reasons: 1, reasons_dropped: 1, model_calls: 4 }],
    ]);
    expect(JSON.stringify(telemetry.trackEvent.mock.calls)).not.toContain('sartén');
  });

  it("counts the nearest facts of a 'sin dato' and an order's sentences among what is shown", () => {
    store.ask('¿lavavajillas?', null);
    turn().flush(
      answer({
        kind: 'NoData',
        products: [],
        reasonsDropped: 0,
        noData: {
          code: 'PAN-1',
          name: 'Sartén',
          sentence: 'La ficha no indica…',
          nearest: [reason('Es de hierro.'), reason('Pesa 2 kg.')],
        },
      }),
    );
    // The product it is about is read as a card, as an answer's products are.
    const cards = http.expectOne((req) => req.url === Cards);
    cards.flush({ items: [hit('PAN-1')] });
    expect(store.exchanges()[0].cards.map((card) => card.code)).toEqual(['PAN-1']);
    expect(telemetry.trackEvent).toHaveBeenLastCalledWith(
      'tino_answered',
      expect.objectContaining({ kind: 'no_data', reasons: 2, products: 0 }),
    );

    store.ask('¿dónde está mi pedido?', null);
    turn().flush(
      answer({
        kind: 'Order',
        products: [],
        says: [reason('Está enviado.', 'Order', 'Su estado es: enviado.')],
        order: { id: 'order-id', title: 'Pedido' },
      }),
    );
    expect(telemetry.trackEvent).toHaveBeenLastCalledWith(
      'tino_answered',
      expect.objectContaining({ kind: 'order', reasons: 1 }),
    );
  });

  // ---------- when Tino does not answer, the search does ----------

  /** Whatever the cause: the shopper's own words, the shop's own search, and a count of why. */
  it.each([
    ['NoProfile', 'no_profile'],
    ['Model', 'model'],
    ['Time', 'time'],
    ['Budget', 'budget'],
    ['Shop', 'shop'],
    ['TooLong', 'too_long'],
    ['Scorer', 'scorer'],
  ] as const)('falls back to the search results when the turn says %s', (cause, counted) => {
    store.ask('una sartén para inducción', null);
    turn().flush(unavailable(cause));

    const search = http.expectOne((req) => req.url === Search);
    expect([search.request.params.get('q'), search.request.params.get('culture')]).toEqual([
      'una sartén para inducción',
      'es',
    ]);
    search.flush({ hits: [hit('PAN-1'), hit('PAN-2')], total: 9, page: 1, pageSize: 4, tookMs: 3 });

    expect([last().state, last().cause, last().found, last().cards.map((card) => card.code)]).toEqual([
      'fallback',
      counted,
      9,
      ['PAN-1', 'PAN-2'],
    ]);
    expect(telemetry.trackEvent.mock.calls).toEqual([['tino_unavailable', { cause: counted }]]);
  });

  /** The request itself failing is the same to a shopper: they asked, and the search answers. */
  it.each([
    [500, 'network'],
    [0, 'network'],
    [429, 'too_many'],
  ] as const)('falls back when the request fails with %i', (status, counted) => {
    store.ask('una sartén', null);
    turn().flush('no', { status, statusText: 'Failed' });
    http
      .expectOne((req) => req.url === Search)
      .flush({ hits: [hit('PAN-1')], total: 1, page: 1, pageSize: 4, tookMs: 3 });

    expect([last().state, last().cause, last().answer]).toEqual(['fallback', counted, null]);
    expect(telemetry.trackEvent).toHaveBeenCalledWith('tino_unavailable', { cause: counted });
  });

  it('falls back to nothing, said as nothing, when the search does not answer either', () => {
    store.ask('una sartén', null);
    turn().flush(unavailable('Model'));
    http.expectOne((req) => req.url === Search).flush('down', { status: 503, statusText: 'Down' });

    expect([last().state, last().cards, last().found, store.busy()]).toEqual(['fallback', [], 0, false]);
  });

  /** The fallback's results are not an answer of Tino's: they are not sent back to it as something it said. */
  it('does not send a fallback as part of the conversation', () => {
    store.ask('una sartén', null);
    turn().flush(unavailable('Time'));
    http
      .expectOne((req) => req.url === Search)
      .flush({ hits: [hit('PAN-1')], total: 1, page: 1, pageSize: 4, tookMs: 3 });

    store.ask('¿y una olla?', null);

    expect(turn().request.body.messages).toEqual([{ role: 'user', text: '¿y una olla?' }]);
  });

  // ---------- one turn at a time ----------

  it('asks nothing for an empty question, nor while a turn is on its way', () => {
    store.ask('   ', null);
    http.expectNone((req) => req.url === Turns);

    store.ask('una sartén', null);
    store.ask('una olla', null);

    expect(http.match((req) => req.url === Turns)).toHaveLength(1);
    expect(store.exchanges().map((exchange) => exchange.question)).toEqual(['una sartén']);
  });

  it('stops a turn on its way: the request is cancelled, and the next question can be asked', () => {
    store.ask('una sartén', null);
    const pending = turn();

    store.stop();

    expect([pending.cancelled, last().state, store.busy()]).toEqual([true, 'stopped', false]);
    store.ask('una olla', null);
    expect(turn().request.body.messages).toEqual([{ role: 'user', text: 'una olla' }]);
  });

  it('starts again with nothing said and no conversation', () => {
    store.ask('una sartén', null);
    turn().flush(answer({ kind: 'Ask', products: [], ask: '¿Cuál?' }));

    store.newConversation();
    store.ask('una olla', null);

    expect(turn().request.body).toEqual({
      messages: [{ role: 'user', text: 'una olla' }],
      context: null,
      conversationId: null,
    });
  });

  /** The head's new conversation: forgotten, and reported with how many questions it had — never what they said. */
  it('starts over when the shopper asks, and reports after how many questions', () => {
    store.ask('una sartén', null);
    turn().flush(answer({ kind: 'Ask', products: [], ask: '¿Cuál?' }));
    telemetry.trackEvent.mockClear();

    store.restart();

    expect([store.exchanges().length, store.conversationId()]).toEqual([0, null]);
    expect(telemetry.trackEvent.mock.calls).toEqual([['tino_conversation_restarted', { exchanges: 1 }]]);
  });
});
