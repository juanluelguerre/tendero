import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ByChannelWidget } from './by-channel-widget';
import { openWidget, setUpWidgets } from './widget-testing';

const AGENTS = (orders: number, agentOrders: number) => ({
  period: 'Week',
  currency: 'EUR',
  orders,
  revenue: orders * 50,
  agentOrders,
  agentRevenue: agentOrders * 50,
  mandated: 0,
  rehearsals: 0,
  byAgent: [],
});

const open = (body: object) => openWidget(ByChannelWidget, { '/api/dashboard/agent-sales': body });

describe('the by channel widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  it('shares the orders between people and agents', async () => {
    const element = await open(AGENTS(40, 10));

    expect([...element.querySelectorAll('.legend li')].map((item) => item.textContent?.trim())).toEqual(['People', 'Agents']);
    expect([...element.querySelectorAll('table td')].map((cell) => cell.textContent?.trim())).toEqual(['30', '10']);
  });

  /** Review focus 5: nothing sold is a sentence, not an empty bar. */
  it('says nothing was sold rather than drawing a share of nothing', async () => {
    const element = await open(AGENTS(0, 0));

    expect(element.querySelector('backoffice-bar-chart')).toBeNull();
    expect(element.textContent).toContain('Nothing sold in this period.');
  });
});
