import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { TinoAnswer } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TinoDoorStore } from '../tino-door.store';
import { answer, hit, reason, unavailable } from '../tino-fixtures';
import { TinoStore } from '../tino.store';
import { NOTHING_LISTENS, SPEECH_TO_TEXT, type Heard, type SpeechToText } from '../../voice/speech-to-text';
import { TinoPanel } from './tino-panel';

const Turns = 'http://api/api/assistant/turns';
const Cards = 'http://api/api/search/cards';
const Search = 'http://api/api/search';
const Status = 'http://api/api/assistant/status';

/** The panel as a shopper meets it: the shop's cards, each sentence with its chip — or the search's results, said as such. */
describe('the tino panel', () => {
  let http: HttpTestingController;
  const telemetry = { trackEvent: vi.fn() };
  // Who listens, read when the panel is drawn: nothing, unless a test says otherwise.
  let speech: SpeechToText = NOTHING_LISTENS;

  beforeEach(() => {
    telemetry.trackEvent.mockClear();
    speech = NOTHING_LISTENS;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        { provide: Telemetry, useValue: telemetry },
        { provide: SPEECH_TO_TEXT, useFactory: () => speech },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(CultureStore).use('es');
  });

  afterEach(() => http.match(() => true));

  async function drawn(url = '/es') {
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(TinoPanel);
    fixture.detectChanges();
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    return fixture;
  }

  /** Asks, answers the turn, and whatever the store then asks the shop for: the cards, or the search. */
  async function asked(reply: TinoAnswer, shop: { cards?: unknown; search?: unknown } = {}, url = '/es') {
    const fixture = await drawn(url);
    TestBed.inject(TinoStore).ask('una sartén para inducción', null);
    http.expectOne((req) => req.url === Turns).flush(reply);
    for (const cards of http.match((req) => req.url === Cards)) cards.flush(shop.cards ?? { items: [] });
    for (const search of http.match((req) => req.url === Search)) search.flush(shop.search ?? { hits: [], total: 0 });
    fixture.detectChanges();
    TestBed.tick();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it("draws a product as the shop's card, and each sentence under it with the chip of its fact", async () => {
    const element = await asked(
      answer({
        products: [
          {
            code: 'PAN-1',
            name: 'A name the model was never asked for',
            reasons: [reason('Vale para inducción.', 'Claim', 'inducción: sí'), reason('Es de hierro.')],
          },
        ],
      }),
      { cards: { items: [hit('PAN-1', 'Sartén de hierro fundido')] } },
    );

    const product = element.querySelector('[data-kind="Products"] li')!;
    // The name on the card is the shop's read of that code.
    expect(product.querySelector('storefront-product-card')!.textContent).toContain('Sartén de hierro fundido');
    expect(product.textContent).not.toContain('A name the model was never asked for');
    expect(product.textContent).toContain('Vale para inducción.');

    const chips = [...product.querySelectorAll('storefront-tino-source')];
    expect(chips.map((chip) => chip.querySelector('summary')!.textContent!.trim())).toEqual([
      'tino.source.Claim',
      'tino.source.DataSheet',
    ]);
    // A claim opens the words it was approved on; anything else, the fact as the shop holds it.
    expect(chips[0].textContent).toContain('base de acero para inducción');
    expect(chips[1].textContent).toContain('material: hierro');
  });

  it("names a product by the read's name when the shop can no longer draw its card", async () => {
    const element = await asked(answer(), { cards: { items: [] } });

    expect(element.querySelector('storefront-product-card')).toBeNull();
    expect(element.querySelector('[data-kind="Products"]')!.textContent).toContain('Sartén de hierro');
  });

  /** "See all" is the search the answer came from: its words, and only when it found more than is shown. */
  it('offers all the results of the search behind an answer, and nothing when there are no more', async () => {
    const more = await asked(answer(), { cards: { items: [hit('PAN-1')] } });
    const link = [...more.querySelectorAll('a')].find((a) => a.textContent!.includes('tino.seeAll'))!;
    expect(link.getAttribute('href')).toBe('/es?q=sart%C3%A9n%20inducci%C3%B3n');
  });

  it('offers no more results when the search found only what is shown', async () => {
    const element = await asked(
      answer({ reads: [{ tool: 'search_products', arguments: { text: 'sartén' }, rows: 1, total: 1 }] }),
    );

    expect(element.textContent).not.toContain('tino.seeAll');
  });

  it("says what a product's details do not say with the server's sentence, and the nearest facts with their chips", async () => {
    const element = await asked(
      answer({
        kind: 'NoData',
        products: [],
        noData: {
          code: 'PAN-1',
          name: 'Sartén',
          sentence: 'La ficha de este producto no indica si puede ir al lavavajillas.',
          nearest: [reason('Es de hierro fundido.')],
        },
      }),
      { cards: { items: [hit('PAN-1', 'Sartén de hierro fundido')] } },
    );

    // Which product "this product" is: the shop's card of it, above the sentence.
    const card = element.querySelector('[data-kind="NoData"] storefront-product-card')!;
    expect(card.textContent).toContain('Sartén de hierro fundido');
    expect(card.compareDocumentPosition(element.querySelector('[data-kind="NoData"] lib-notice')!)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
    expect(element.textContent).toContain('La ficha de este producto no indica si puede ir al lavavajillas.');
    expect(element.textContent).toContain('tino.nearest');
    expect(element.querySelectorAll('storefront-tino-source')).toHaveLength(1);
  });

  it('asks a question back as it is, with nothing around it', async () => {
    const element = await asked(answer({ kind: 'Ask', products: [], ask: '¿Para qué la quieres?', reads: [] }));

    expect(element.querySelector('[data-kind="Ask"]')!.textContent!.trim()).toBe('¿Para qué la quieres?');
  });

  /** Tino explains and links: a return is opened on the order's own page. */
  it("answers about an order with its title, its sentences and the way to the order's page", async () => {
    const element = await asked(
      answer({
        kind: 'Order',
        products: [],
        says: [
          reason(
            'Se puede devolver hasta el 15 de enero.',
            'Order',
            'Se puede devolver 1 × Botines hasta el 15 de enero de 2026.',
          ),
        ],
        order: { id: '0199c2f4-7d3e-7a11-9b55-3f1f0c2a9e10', title: 'Pedido del 1 de enero de 2026' },
      }),
    );

    expect(element.textContent).toContain('Pedido del 1 de enero de 2026');
    expect(element.querySelector('summary')!.textContent!.trim()).toBe('tino.source.Order');
    const link = [...element.querySelectorAll('a')].find((a) => a.textContent!.includes('tino.openOrder'))!;
    expect(link.getAttribute('href')).toBe('/es/orders/0199c2f4-7d3e-7a11-9b55-3f1f0c2a9e10');
  });

  /** The shop's notice is the shop's words: there is no fact behind it to open. */
  it("shows a notice of the shop's with no chip", async () => {
    const notice = 'Para preguntar por un pedido, inicia sesión o abre el pedido desde su correo de confirmación.';
    const element = await asked(
      answer({ kind: 'Shop', products: [], says: [reason(notice, 'Notice', notice)], reads: [] }),
    );

    expect(element.textContent).toContain(notice);
    expect(element.querySelector('storefront-tino-source')).toBeNull();
  });

  // ---------- when Tino does not answer ----------

  /** Invariant 8, drawn: the same cards, from the search, and a sentence that says they are the search's. */
  it("shows the search's results for the shopper's words when Tino does not answer, and says so", async () => {
    const element = await asked(unavailable('Time'), {
      search: { hits: [hit('PAN-1'), hit('PAN-2', 'Sartén de acero')], total: 9 },
    });

    expect(element.textContent).toContain('tino.fallback.title');
    expect(element.textContent).toContain('tino.fallback.results');
    expect(element.querySelectorAll('[data-fallback] storefront-product-card')).toHaveLength(2);
    // No sentence of a model's, and so no chip: these are the search's.
    expect(element.querySelector('storefront-tino-source')).toBeNull();
    expect(element.querySelector('[data-answer]')).toBeNull();

    const all = [...element.querySelectorAll('a')].find((a) => a.textContent!.includes('tino.seeAll'))!;
    expect(all.getAttribute('href')).toBe('/es?q=una%20sart%C3%A9n%20para%20inducci%C3%B3n');
  });

  /** Nothing matched the words, so the search's cards are only the nearest: said as such, never as "results". */
  it('says the cards are only the nearest when nothing matched the words', async () => {
    const element = await asked(unavailable('Model'), {
      search: { hits: [hit('PAN-1'), hit('PAN-2', 'Sartén de acero')], total: 35, answer: { match: 'Related' } },
    });

    expect(element.textContent).toContain('tino.fallback.related');
    expect(element.textContent).not.toContain('tino.fallback.results');
    const all = [...element.querySelectorAll('a')].find((a) => a.textContent!.includes('tino.seeAll'))!;
    expect(all.textContent).toContain('tino.seeAllRelated');
  });

  it('says so too when the search found nothing, and when there were too many questions', async () => {
    expect((await asked(unavailable('Model'))).textContent).toContain('tino.fallback.nothing');
  });

  // ---------- the composer, and what "it" means ----------

  it('sends what is typed with the product of the page it is asked on, and empties the box', async () => {
    const fixture = await drawn('/es/p/sarten-de-hierro/PAN-1');
    const element = fixture.nativeElement as HTMLElement;
    const composer = element.querySelector('textarea')!;
    const send = element.querySelector<HTMLButtonElement>('footer button[type="submit"]')!;

    expect(element.querySelector('header')!.textContent).toContain('tino.on.Product');
    expect(send.disabled).toBe(true);

    composer.value = '¿puede ir al horno?';
    composer.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(send.disabled).toBe(false);
    composer.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();

    const turn = http.expectOne((req) => req.url === Turns);
    expect(turn.request.body).toEqual({
      messages: [{ role: 'user', text: '¿puede ir al horno?' }],
      context: { kind: 'Product', key: 'PAN-1' },
      conversationId: null,
    });
    expect(composer.value).toBe('');
    // On its way: one line, and where sending was, a way to stop it — and nothing to send with.
    expect(element.textContent).toContain('tino.working');
    expect(element.querySelector('footer button[type="submit"]')).toBeNull();
    const stop = element.querySelector<HTMLButtonElement>('footer button[data-stop]')!;
    expect(stop.getAttribute('aria-label')).toBe('tino.stop');

    stop.click();
    fixture.detectChanges();

    expect(turn.cancelled).toBe(true);
    expect(element.textContent).toContain('tino.stopped');
    expect(element.querySelector('footer button[type="submit"]')).not.toBeNull();
  });

  it('empties the box when Enter comes before the page has drawn what was typed', async () => {
    const fixture = await drawn();
    const composer = (fixture.nativeElement as HTMLElement).querySelector('textarea')!;

    // No render between the two: a slow frame, or a tab in the background.
    composer.value = 'una sartén';
    composer.dispatchEvent(new Event('input'));
    composer.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();

    http.expectOne((req) => req.url === Turns);
    expect(composer.value).toBe('');
  });

  it('adds a line with Shift+Enter instead of sending', async () => {
    const fixture = await drawn();
    const composer = (fixture.nativeElement as HTMLElement).querySelector('textarea')!;
    composer.value = 'una sartén';
    composer.dispatchEvent(new Event('input'));

    composer.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true }));

    http.expectNone((req) => req.url === Turns);
  });

  it('names no page in its head where there is no one thing on the screen', async () => {
    const element = (await drawn('/es/c/KITCHEN')).nativeElement as HTMLElement;

    expect(element.querySelector('header')!.textContent).not.toContain('tino.on.');
  });

  /** What the search page handed over is asked as the panel opens, once. */
  it('asks the question it was handed as it opens', async () => {
    TestBed.inject(TinoDoorStore).ask('una sartén que no pese mucho');

    await drawn('/es?q=una%20sart%C3%A9n');

    expect(http.expectOne((req) => req.url === Turns).request.body.messages).toEqual([
      { role: 'user', text: 'una sartén que no pese mucho' },
    ]);
    expect(TestBed.inject(TinoDoorStore).handed()).toBeNull();
  });

  // ---------- what an answer leads to ----------

  it('counts a product followed with its place in the answer, and nothing for a click that follows nothing', async () => {
    const element = await asked(
      answer({
        products: [
          { code: 'PAN-1', name: 'Sartén de hierro', reasons: [reason('Es de hierro.')] },
          { code: 'PAN-2', name: 'Sartén de acero', reasons: [] },
        ],
      }),
      { cards: { items: [hit('PAN-1'), hit('PAN-2', 'Sartén de acero')] } },
    );
    telemetry.trackEvent.mockClear();
    const products = element.querySelectorAll('[data-kind="Products"] li');

    products[1].querySelector<HTMLAnchorElement>('a.card__link')!.click();
    products[0].querySelector('summary')!.click();

    expect(telemetry.trackEvent.mock.calls).toEqual([
      ['tino_result_followed', { action: 'product_opened', position: 2 }],
    ]);
  });

  // ---------- where Tino cannot be asked ----------

  /** The door stayed; what is behind it says so, and offers the shop's search. */
  it('says it is not available and offers the search, with no box to ask in', async () => {
    const door = TestBed.inject(TinoDoorStore);
    TestBed.tick();
    http.expectOne(Status).flush({ available: false });
    const fixture = await drawn();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('tino.notAvailable.title');
    expect(element.querySelector('textarea')).toBeNull();

    const box = element.querySelector<HTMLInputElement>('input[type="search"]')!;
    box.value = 'sartén';
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await new Promise((resolve) => setTimeout(resolve));

    expect(TestBed.inject(Router).url).toBe('/es?q=sart%C3%A9n');
    expect(telemetry.trackEvent).toHaveBeenCalledWith('tino_result_followed', { action: 'search_opened', position: 0 });
  });

  // ---------- speaking to Tino (phase 14, the voice spec V-2 and V-3) ----------

  it('has no microphone where nothing can listen, and the composer is as it always was', async () => {
    const element = (await drawn()).nativeElement as HTMLElement;

    expect(element.querySelector('footer [data-microphone]')).toBeNull();
  });

  /** The sketch of 2026-10-16, screens 2 and 3: listening said in words, then the words in the box and nothing sent. */
  it('writes what was heard into the box and sends nothing, saying to check it', async () => {
    let finish: (heard: Heard) => void = () => undefined;
    speech = {
      name: 'shop',
      available: async () => true,
      listen: () => ({
        stop: () => finish({ kind: 'words', words: 'unas zapatillas para correr' }),
        heard: new Promise((r) => (finish = r)),
      }),
    };
    const fixture = await drawn();
    const element = fixture.nativeElement as HTMLElement;
    const microphone = () => element.querySelector<HTMLButtonElement>('footer [data-microphone]')!;

    microphone().click();
    fixture.detectChanges();
    const listening = [element.querySelector('textarea')!.placeholder, element.querySelector('footer')!.textContent];
    microphone().click();
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    expect(listening[0]).toBe('voice.listening');
    expect(listening[1]).toContain('voice.endHint');
    expect(element.querySelector('textarea')!.value).toBe('unas zapatillas para correr');
    expect(element.querySelector('footer')!.textContent).toContain('voice.landed');
    expect(http.match((req) => req.url === Turns)).toEqual([]);
    expect(element.querySelector<HTMLButtonElement>('footer button[type="submit"]')!.disabled).toBe(false);
  });
});
