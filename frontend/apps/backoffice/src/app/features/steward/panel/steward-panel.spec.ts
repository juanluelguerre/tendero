import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { StewardAnswer } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StewardStore } from '../steward.store';
import { StewardPanel } from './steward-panel';

const Turns = 'http://api/api/steward/turns';
const scrolledIntoView = vi.fn();
const Proposals = 'http://api/api/steward/proposals';

const answer = (extra: Partial<StewardAnswer>): StewardAnswer => ({
  status: 'Answered',
  text: 'Twelve have no English name.',
  reads: [{ tool: 'catalogue_gaps', rows: 12, total: 12 }],
  proposalIds: [],
  cost: { calls: 3, inputTokens: 1700, outputTokens: 142, microEuros: 2100, milliseconds: 4100, model: 'qwen' },
  profile: 'local',
  fellBack: false,
  nearContextLimit: false,
  conversationId: 'conv-1',
  ...extra,
});

/** The panel as a shopkeeper meets it: what it read, the words, the card, the cost — or, with no model, the way on without one. */
describe('the steward panel', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.open = true;
    };
    // jsdom lays nothing out, so it has nothing to scroll into view.
    Element.prototype.scrollIntoView = scrolledIntoView;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function asked(reply: StewardAnswer) {
    const fixture = TestBed.createComponent(StewardPanel);
    fixture.detectChanges();
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    // Not whenStable either: the rail's badges are asked for and never answered here.
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    TestBed.inject(StewardStore).ask('Which products have no English name?', null);
    http.expectOne((req) => req.url === Turns).flush(reply);
    // Not whenStable: a card's proposals are still on their way, which is what one test asks about.
    fixture.detectChanges();
    TestBed.tick();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows what it read, its answer and what the conversation cost', async () => {
    const element = await asked(answer({}));

    expect(element.textContent).toContain('steward.read');
    expect(element.textContent).toContain('Twelve have no English name.');
    expect(element.textContent).toContain('steward.cost');
  });

  /** What the model writes with Markdown is drawn as such, and markup it writes stays text. */
  it('draws the rows a read returned as a table, and none for a read with no rows', async () => {
    const table = (rows: number) => ({
      key: 'catalogueGaps',
      currency: null,
      columns: [{ key: 'code', kind: 'Text' as const }],
      rows: Array.from({ length: rows }, (_, i) => [{ text: `MUG-${i}`, number: null }]),
    });
    const element = await asked(
      answer({
        reads: [
          { tool: 'catalogue_gaps', rows: 2, total: 2, tables: [table(2)] },
          { tool: 'find_products', rows: 0, total: 0, tables: [{ ...table(0), key: 'foundProducts' }] },
        ],
      }),
    );

    expect(element.querySelectorAll('backoffice-steward-table')).toHaveLength(1);
    expect(element.querySelectorAll('backoffice-steward-table td')).toHaveLength(2);
  });

  it("draws the answer's Markdown: bold, a list and a table, and never its HTML", async () => {
    const element = await asked(
      answer({
        text: 'Hay **22 productos**.\n\n1. Uno\n2. Dos\n\n| Producto | Ventas |\n|---|---|\n| Bolsa | 10 € |\n\n<img src=x onerror=alert(1)>',
      }),
    );

    expect(element.querySelector('strong')?.textContent).toBe('22 productos');
    expect([...element.querySelectorAll('ol li')].map((item) => item.textContent?.trim())).toEqual(['Uno', 'Dos']);
    expect(element.querySelector('table td')?.textContent?.trim()).toBe('Bolsa');
    expect(element.querySelector('img')).toBeNull();
    expect(element.textContent).toContain('<img src=x onerror=alert(1)>');
  });

  /** scrollIntoView scrolls the page too, and left the backoffice scrolled to its end with a blank below (author's run, 2026-10-03). */
  it('scrolls its own conversation to the new question and never the page', async () => {
    scrolledIntoView.mockClear();

    await asked(answer({}));

    expect(scrolledIntoView).not.toHaveBeenCalled();
  });

  /** A disabled field loses the focus, and nobody gave it back: the next question needed a click. */
  it('keeps the composer for the next question while an answer is on its way, and gets the focus back after it', async () => {
    const fixture = TestBed.createComponent(StewardPanel);
    fixture.detectChanges();
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    const composer = (fixture.nativeElement as HTMLElement).querySelector('textarea') as HTMLTextAreaElement;
    composer.focus();

    TestBed.inject(StewardStore).ask('One', null);
    fixture.detectChanges();
    expect(composer.disabled).toBe(false);

    composer.blur();
    http.expectOne((req) => req.url === Turns).flush(answer({}));
    fixture.detectChanges();
    TestBed.tick();

    expect(document.activeElement).toBe(composer);
  });

  /**
   * Every question carries a screen-reader-only span, which is `position: absolute`: in a
   * scroller that is not positioned it is neither contained nor clipped, and it lands far
   * down the page, which grows with the conversation (found in the author's browser, 2026-10-03).
   */
  it('positions its scroller, so what is only for a screen reader cannot stretch the page', async () => {
    const element = await asked(answer({}));

    const scroller = element.querySelector('.overflow-y-auto');
    expect(scroller?.classList.contains('relative')).toBe(true);
    expect(scroller?.querySelector('.sr-only')).not.toBeNull();
  });

  it('brings the proposals as a card, read by their ids', async () => {
    await asked(answer({ proposalIds: ['p1', 'p2'] }));

    const card = http.expectOne((req) => req.url === Proposals && req.params.has('ids'));
    expect(card.request.params.getAll('ids')).toEqual(['p1', 'p2']);
  });

  /** The head's new conversation: there beside the close, idle while nothing has been said, and it starts over. */
  it('offers a new conversation in its head once something has been said, and starts over from there', async () => {
    const fixture = TestBed.createComponent(StewardPanel);
    fixture.detectChanges();
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const restart = () =>
      element.querySelector<HTMLButtonElement>('header button[aria-label="steward.newConversation"]');
    const idle = restart()?.disabled;

    const store = TestBed.inject(StewardStore);
    store.ask('Which products have no English name?', null);
    http.expectOne((req) => req.url === Turns).flush(answer({}));
    fixture.detectChanges();
    const restarted = vi.spyOn(store, 'restart');
    restart()!.click();

    expect([idle, restarted.mock.calls]).toEqual([true, [['header']]]);
  });

  /** Where Steward itself asks for a new conversation, that is where the restart says it came from. */
  it('starts over from the notice near the window, saying so', async () => {
    const near = await asked(answer({ nearContextLimit: true }));
    const store = TestBed.inject(StewardStore);
    const restarted = vi.spyOn(store, 'restart');
    near.querySelector<HTMLButtonElement>('lib-notice button')!.click();

    expect(restarted.mock.calls).toEqual([['near_limit']]);
  });

  it('starts over from the button that replaces the composer once the conversation is over', async () => {
    const ended = await asked(answer({ status: 'ConversationTooLong', text: null, reads: [], cost: null }));
    const restarted = vi.spyOn(TestBed.inject(StewardStore), 'restart');
    ended.querySelector<HTMLButtonElement>('footer button')!.click();

    expect(restarted.mock.calls).toEqual([['ended']]);
  });

  /** Invariant 8 for Steward: no model, and the shop is still usable — the same list as a filter, and Proposals. */
  it('says no model answered and offers the way on without one', async () => {
    const element = await asked(answer({ status: 'Unavailable', text: null, reads: [], cost: null }));

    const links = [...element.querySelectorAll('a')].map((link) => link.getAttribute('href'));
    expect(element.textContent).toContain('steward.unavailable.title');
    expect(links).toEqual(['/catalogue?status=all&missing=en', '/steward/proposals']);
  });
});
