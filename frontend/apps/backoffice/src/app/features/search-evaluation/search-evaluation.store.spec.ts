import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SearchEvaluationStore } from './search-evaluation.store';

const Evaluation = 'http://api/api/search/evaluation';

const score = (ndcg: number | null) => ({
  ndcgAt10: ndcg,
  recallAt50: 0.9,
  meanMilliseconds: 12,
  thresholdNdcgAt10: 0.8,
  thresholdRecallAt50: 0.8,
});

/** The search evaluation's state (ADR 0047): nothing until asked, then running, then the measure or the failure. */
describe('the search evaluation store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        SearchEvaluationStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  /** It runs the whole golden set: never on opening the screen, only when somebody asks. */
  it('measures nothing until it is run', () => {
    const store = TestBed.inject(SearchEvaluationStore);
    TestBed.tick();

    expect([store.status(), http.match(Evaluation).length]).toEqual(['idle', 0]);
  });

  it('runs, then shows each language with the query hybrid lost the most first', () => {
    const store = TestBed.inject(SearchEvaluationStore);
    store.run();
    const running = store.status();
    http.expectOne(Evaluation).flush({
      vectorLayer: { isAvailable: true, isConfigured: true },
      judgedProductsInCatalogue: 100,
      elapsedSeconds: 3,
      cultures: [
        {
          culture: 'es',
          lexical: score(0.8),
          hybrid: score(0.85),
          queries: [
            { query: 'mochila', lexicalNdcgAt10: 0.9, hybridNdcgAt10: 0.95 },
            { query: 'botas', lexicalNdcgAt10: 0.9, hybridNdcgAt10: 0.7 },
          ],
        },
      ],
    });

    expect([
      running,
      store.status(),
      store.cultures()[0].rows.map((row) => row.query),
      store.cultures()[0].ndcg.trend,
    ]).toEqual(['running', 'ready', ['botas', 'mochila'], 'better']);
  });

  it('says it failed when the server does', () => {
    const store = TestBed.inject(SearchEvaluationStore);
    store.run();
    http.expectOne(Evaluation).flush('down', { status: 503, statusText: 'Unavailable' });

    expect(store.status()).toBe('failed');
  });
});
