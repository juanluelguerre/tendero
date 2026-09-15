import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SearchEvaluationPage } from './search-evaluation-page';

const EVALUATION_URL = 'http://api/api/search/evaluation';

/**
 * The search evaluation panel (P8-11). What is asserted is the part a template
 * could get wrong without the server noticing: that it does not spend seconds on
 * opening, that the query hybrid lost on is the one read first, and that two
 * identical columns are explained rather than left to be misread.
 */
describe('the search evaluation page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SearchEvaluationPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function open() {
    const fixture = TestBed.createComponent(SearchEvaluationPage);
    fixture.detectChanges();

    // `*transloco` renders nothing until its language arrives. Empty, because
    // what is asserted is order, numbers and requests, not words.
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});

    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  async function runWith(result: ReturnType<typeof evaluation>) {
    const fixture = await open();
    const element = fixture.nativeElement as HTMLElement;

    button(element).click();
    fixture.detectChanges();

    http.expectOne(EVALUATION_URL).flush(result);
    fixture.detectChanges();
    await fixture.whenStable();

    return element;
  }

  it('measures nothing until somebody asks', async () => {
    const fixture = await open();

    http.expectNone(EVALUATION_URL);

    button(fixture.nativeElement as HTMLElement).click();
    fixture.detectChanges();

    http.expectOne(EVALUATION_URL);
  });

  it('puts the query hybrid lost on first, with its signed difference', async () => {
    const element = await runWith(evaluation(available));

    const rows = [...element.querySelectorAll('table.queries tbody tr')].map((row) =>
      [...row.querySelectorAll('td')].map((cell) => (cell.textContent ?? '').trim()),
    );

    expect(rows.map((row) => row[0])).toEqual(['zapatilas', 'mochila urbana', 'regalo para casa', 'sin juicios']);
    expect(rows[0][3]).toMatch(/^[−-]0[.,]200$/);
    expect(rows[1][3]).toMatch(/^0[.,]000$/);
    expect(rows[2][3]).toMatch(/^\+0[.,]307$/);
    expect(rows[3][3]).toBe('—');

    // The colour repeats the sign, so it is asserted too: a floating-point
    // 5.5e-17 prints as 0,000 either way, and only the trend shows whether it
    // was painted as an improvement nobody can see the reason for.
    const trends = [...element.querySelectorAll('table.queries td.delta')].map((cell) =>
      [...cell.classList].find((name) => name.startsWith('delta--')),
    );

    expect(trends).toEqual(['delta--worse', 'delta--same', 'delta--better', 'delta--same']);
  });

  it('says why hybrid repeats lexical when the vector layer is down', async () => {
    const reason = 'The model server is still downloading its weights.';
    const element = await runWith(evaluation({ isConfigured: true, isAvailable: false, reason }));

    expect(element.querySelector('.reason')?.textContent?.trim()).toBe(reason);
  });

  it('shows no excuse when the vector layer answered', async () => {
    const element = await runWith(evaluation(available));

    expect(element.querySelector('.reason')).toBeNull();
    expect(element.querySelector('.notice')).toBeNull();
  });
});

/** Before a run there is exactly one button, the clay one; a missing one is the failure. */
function button(element: HTMLElement): HTMLButtonElement {
  const found = element.querySelector<HTMLButtonElement>('button');
  if (!found) throw new Error('The page offers no button to run the evaluation.');
  return found;
}

const available = { isConfigured: true, isAvailable: true, reason: 'bge-m3 at 5617a9f, collection ready.' };

function evaluation(vectorLayer: { isConfigured: boolean; isAvailable: boolean; reason: string }) {
  return {
    vectorLayer,
    judgedProductsInCatalogue: 100,
    elapsedSeconds: 3.4,
    cultures: [
      {
        culture: 'es',
        lexical: score(0.927, 0.939),
        hybrid: score(0.931, 0.95),
        // Golden order, deliberately not the order the screen shows.
        queries: [
          // 0.1 + 0.2 is 0.30000000000000004: floating point, not relevance, and
          // the screen must call it "same" rather than "+0.000".
          query('mochila urbana', 0.3, 0.1 + 0.2),
          query('regalo para casa', 0.305, 0.612),
          query('sin juicios', null, null),
          query('zapatilas', 1, 0.8),
        ],
      },
    ],
  };
}

function score(ndcgAt10: number, recallAt50: number) {
  return {
    ndcgAt10,
    recallAt50,
    thresholdNdcgAt10: 0.91,
    thresholdRecallAt50: 0.92,
    meetsThresholds: true,
    meanMilliseconds: 42,
  };
}

function query(text: string, lexical: number | null, hybrid: number | null) {
  return {
    query: text,
    lexicalNdcgAt10: lexical,
    hybridNdcgAt10: hybrid,
    lexicalRecallAt50: lexical,
    hybridRecallAt50: hybrid,
  };
}
