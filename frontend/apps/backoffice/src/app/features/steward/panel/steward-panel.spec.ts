import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { StewardAnswer } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { StewardStore } from '../steward.store';
import { StewardPanel } from './steward-panel';

const Turns = 'http://api/api/steward/turns';
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

  it('brings the proposals as a card, read by their ids', async () => {
    await asked(answer({ proposalIds: ['p1', 'p2'] }));

    const card = http.expectOne((req) => req.url === Proposals && req.params.has('ids'));
    expect(card.request.params.getAll('ids')).toEqual(['p1', 'p2']);
  });

  /** Invariant 8 for Steward: no model, and the shop is still usable — the same list as a filter, and Proposals. */
  it('says no model answered and offers the way on without one', async () => {
    const element = await asked(answer({ status: 'Unavailable', text: null, reads: [], cost: null }));

    const links = [...element.querySelectorAll('a')].map((link) => link.getAttribute('href'));
    expect(element.textContent).toContain('steward.unavailable.title');
    expect(links).toEqual(['/catalogue?status=all&missing=en', '/steward/proposals']);
  });
});
