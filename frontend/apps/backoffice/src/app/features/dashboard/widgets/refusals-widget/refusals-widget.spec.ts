import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { RefusalsWidget } from './refusals-widget';
import { openWidget, setUpWidgets } from '../widget-testing';

const DENIALS = {
  period: 'Week',
  total: 3,
  byReason: [
    { reason: 'mandate_required', count: 2 },
    { reason: 'orders_per_hour', count: 1 },
  ],
  byAgent: [],
  recent: [
    {
      agentId: 'agent-7',
      actorName: null,
      command: 'complete_checkout',
      reason: 'mandate_required',
      detail: null,
      at: '2026-10-12T10:00:00Z',
    },
  ],
};

describe('the refusals widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  it('ranks the refusals by rule in rows and keeps the latest with their agent', async () => {
    const element = await openWidget(RefusalsWidget, { '/api/dashboard/agent-denials': DENIALS });

    expect([...element.querySelectorAll('table tbody th')].map((cell) => cell.textContent?.trim())).toEqual([
      'No mandate',
      'Too many orders an hour',
    ]);
    expect(element.querySelector('.latest__row')?.textContent).toContain('agent-7');
  });

  it('says nothing was refused rather than drawing no bars', async () => {
    const element = await openWidget(RefusalsWidget, {
      '/api/dashboard/agent-denials': { ...DENIALS, total: 0, byReason: [], recent: [] },
    });

    expect(element.querySelector('backoffice-bar-chart')).toBeNull();
    expect(element.textContent).toContain('Nothing refused in this period.');
  });
});
