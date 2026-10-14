import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ByChannelWidget } from './by-channel-widget';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { openWidget, openWidgetFixture, setUpWidgets } from '../widget-testing';

/** Money as the API sends it: minor units and its currency. */
const eur = (amount: number) => ({ amount: Math.round(amount * 100), currency: 'EUR' });

/** The period's sales by the channel each order recorded (R-9, D-7): the storefront, MCP and UCP, and those before channels. */
const MIX = (web: number, mcp: number, ucp: number, unrecorded = 0) => ({
  period: 'Week',
  byChannel: [
    { channel: 'Web', orders: web, revenue: eur(web * 50) },
    { channel: 'Mcp', orders: mcp, revenue: eur(mcp * 50) },
    { channel: 'Ucp', orders: ucp, revenue: eur(ucp * 50) },
  ],
  unrecorded: { channel: null, orders: unrecorded, revenue: eur(unrecorded * 50) },
  byCategory: [],
});

const open = (body: object) => openWidget(ByChannelWidget, { '/api/dashboard/mix': body });

describe('the by channel widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => {
    TestBed.inject(HttpTestingController).match(() => true);
    vi.restoreAllMocks();
  });

  it('shares the orders between people and agents, and says agents by MCP and UCP', async () => {
    const element = await open(MIX(30, 7, 3));

    expect([...element.querySelectorAll('.legend li')].map((item) => item.textContent?.trim())).toEqual([
      'People',
      'Agents',
    ]);
    expect([...element.querySelectorAll('table td')].map((cell) => cell.textContent?.trim())).toEqual(['30', '10']);
    expect(element.textContent).toContain('Agents: 7 over MCP · 3 over UCP');
  });

  it('says how many earlier orders have no channel, rather than counting them as the storefront', async () => {
    const element = await open(MIX(30, 7, 3, 4));

    expect(element.textContent).toContain('4 earlier orders have no channel recorded');
  });

  /** Only orders before channels: something sold — just not through a channel the shop recorded (the branch's review). */
  it('does not say nothing sold when every order predates the channel', async () => {
    const element = await open(MIX(0, 0, 0, 64));

    expect(element.textContent).not.toContain('Nothing sold in this period.');
    expect(element.textContent).toContain('Nothing sold through a recorded channel in this period.');
  });

  /** Review focus 5: nothing sold is a sentence, not an empty bar. */
  it('says nothing was sold rather than drawing a share of nothing', async () => {
    const element = await open(MIX(0, 0, 0));

    expect(element.querySelector('backoffice-bar-chart')).toBeNull();
    expect(element.textContent).toContain('Nothing sold in this period.');
  });

  /** A wall refreshes every minute: its figures stay while the new ones are asked for, never a blink of "Loading…". */
  it('keeps its figures on screen while a refresh asks again', async () => {
    const fixture = await openWidgetFixture(ByChannelWidget, { '/api/dashboard/mix': MIX(30, 7, 3) });
    const later = Date.now() + 60_000;
    vi.spyOn(Date, 'now').mockReturnValue(later);
    TestBed.inject(DashboardRefresh).refresh();
    fixture.detectChanges();
    TestBed.tick();
    const element = fixture.nativeElement as HTMLElement;

    TestBed.inject(HttpTestingController).expectOne((req) => req.url === 'http://api/api/dashboard/mix');
    expect(element.textContent).not.toContain('Loading');
    expect([...element.querySelectorAll('table td')].map((cell) => cell.textContent?.trim())).toEqual(['30', '10']);
  });
});
