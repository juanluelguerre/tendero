import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import type { KitSentenceUnderstood } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { Observable, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { KitService } from '../../data-access/kit.service';
import { KitsStore } from './kits.store';

const eur = (amount: number) => ({ amount, currency: 'EUR' });

/**
 * The kits' list, and the box above it (phase 13c, K-6): a sentence read into a
 * kit's form, and the shopper taken to that kit with the form filled. The
 * model only names the kit; the rest is code, and nothing is built here.
 */
describe('the kits store', () => {
  let available: () => Observable<{ available: boolean }>;
  let reading: () => Observable<KitSentenceUnderstood>;
  let asked: { sentence: string; culture: string }[];
  let went: { commands: unknown[]; extras: unknown }[];
  let events: unknown[][];

  beforeEach(() => {
    available = () => of({ available: true });
    reading = () =>
      of({ template: 'KITCHEN_STARTER', budget: eur(20000), requirements: ['INDUCTION:true'], understood: true });
    asked = [];
    went = [];
    events = [];
  });

  async function opened() {
    TestBed.configureTestingModule({
      providers: [
        KitsStore,
        { provide: CultureStore, useValue: { active: signal('es') } },
        {
          provide: Router,
          useValue: {
            navigate: async (commands: unknown[], extras: unknown) => void went.push({ commands, extras }),
          },
        },
        { provide: Telemetry, useValue: { trackEvent: (...event: unknown[]) => void events.push(event) } },
        {
          provide: KitService,
          useValue: {
            list: () => of([]),
            sentenceStatus: () => available(),
            understand: (sentence: string, culture: string) => (asked.push({ sentence, culture }), reading()),
          },
        },
      ],
    });
    const store = TestBed.inject(KitsStore);
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('offers the box only when the shop can read a sentence', async () => {
    expect((await opened()).offersSentence()).toBe(true);

    TestBed.resetTestingModule();
    available = () => of({ available: false });
    expect((await opened()).offersSentence()).toBe(false);

    // A status that cannot be read is no box: the list is what there is (invariant 8).
    TestBed.resetTestingModule();
    available = () => throwError(() => ({ status: 503 }));
    expect((await opened()).offersSentence()).toBe(false);
  });

  it('takes the shopper to the kit it named, with the budget and the requirements in the address', async () => {
    const store = await opened();
    store.sentence.set({ text: ' un kit de cocina para inducción por menos de 200 € ' });

    await store.understand();

    expect(asked).toEqual([{ sentence: 'un kit de cocina para inducción por menos de 200 €', culture: 'es' }]);
    expect(went).toEqual([
      {
        commands: ['/', 'es', 'kits', 'KITCHEN_STARTER'],
        extras: { queryParams: { budget: '200', needs: 'INDUCTION:true' }, state: { kitSource: 'sentence' } },
      },
    ]);
    expect(events).toEqual([['kit_sentence_read', { understood: true, template: 'KITCHEN_STARTER', budget: true }]]);
  });

  it('leaves the budget out of the address when the sentence said none', async () => {
    reading = () => of({ template: 'COFFEE_CORNER', budget: null, requirements: [], understood: true });
    const store = await opened();
    store.sentence.set({ text: 'algo para hacer café' });

    await store.understand();

    expect(went[0]?.extras).toEqual({ queryParams: { budget: null, needs: null }, state: { kitSource: 'sentence' } });
  });

  it('stays on the list and says so when the sentence named no kit, or could not be read', async () => {
    reading = () => of({ template: null, budget: null, requirements: [], understood: false });
    const store = await opened();
    store.sentence.set({ text: 'algo bonito' });

    await store.understand();

    expect([went, store.notUnderstood(), store.reading()]).toEqual([[], true, false]);
    expect(events).toEqual([['kit_sentence_read', { understood: false, template: 'none', budget: false }]]);

    reading = () => throwError(() => ({ status: 503 }));
    await store.understand();
    expect([went, store.notUnderstood()]).toEqual([[], true]);
  });

  it('asks nothing for an empty sentence', async () => {
    const store = await opened();
    store.sentence.set({ text: '   ' });

    await store.understand();

    expect(asked).toEqual([]);
  });
});
