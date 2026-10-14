import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { AgentView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { UndoStore } from '../../worklist/undo/undo.store';
import { AgentsStore } from './agents.store';

const Agents = 'http://api/api/agents';

const agent = (agentId: string) =>
  ({ agentId, displayName: agentId, operator: 'Acme', trust: 'Known' }) as unknown as AgentView;

/** The registry's shared state (ADR 0047): the agents, the open one, the draft to register, and the changes. */
describe('the agents store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        AgentsStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(AgentsStore);
    TestBed.tick();
    http
      .match((req) => req.url === Agents && req.method === 'GET')
      .forEach((req) => req.flush([agent('a-1'), agent('a-2')]));
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('opens the agent the URL names', async () => {
    const store = await at('/?agent=a-2');

    expect(store.selected()?.agentId).toBe('a-2');
  });

  it('registers the draft, trimmed, then clears it and reads the registry again', async () => {
    const store = await at('/');
    store.edit('agentId', '  a-3 ');
    store.edit('displayName', 'Shopper bot');
    store.register();
    const sent = http.expectOne((req) => req.url === Agents && req.method === 'POST');
    const body = sent.request.body;
    sent.flush({});
    TestBed.tick();

    expect([
      body.agentId,
      store.draft().agentId,
      http.match((req) => req.url === Agents && req.method === 'GET').length,
    ]).toEqual(['a-3', '', 1]);
  });

  /** Suspending asks a reason first, and offers Undo — which is reinstating, on the server. */
  it('suspends with a reason and offers to undo it', async () => {
    const store = await at('/');
    store.askToSuspend(agent('a-1'), 'pane');
    store.suspend('Too many orders');
    http.expectOne(`${Agents}/a-1/suspend`).flush({});
    TestBed.inject(UndoStore).take();

    expect(http.match(`${Agents}/a-1/reinstate`)).toHaveLength(1);
  });

  it("shows the server's refusal", async () => {
    const store = await at('/');
    store.trust(agent('a-1'), 'Verified');
    http
      .expectOne(`${Agents}/a-1/trust`)
      .flush(
        { code: 'Refused', status: 409, title: 'Only an operator may verify.' },
        { status: 409, statusText: 'Conflict' },
      );

    expect([store.refusal(), store.busy()]).toEqual(['Only an operator may verify.', null]);
  });
});
