import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Telemetry } from '@tendero/shared-telemetry';
import { describe, expect, it, vi } from 'vitest';
import { BackofficeUsage, outcomeOf, viewOf } from './usage';

/** R-0's backoffice measurement: a screen by its route, an outcome by the server's answer. */
describe('which screen a URL is', () => {
  it.each([
    ['/dashboard', 'dashboard'],
    ['/', 'dashboard'],
    ['/orders?status=Confirmed', 'orders'],
    ['/products/0199a1f0-0000/variants', 'variants'],
    ['/agents#top', 'agents'],
    ['/dashboard/sales', 'dashboard.sales'],
    ['/dashboard/operations?period=day', 'dashboard.operations'],
    ['/dashboard/growth', 'dashboard.growth'],
    ['/dashboard/agents#top', 'dashboard.agents'],
  ])('reads %s as %s', (url, view) => {
    expect(viewOf(url)).toBe(view);
  });

  it('does not measure what is not a shopkeeper’s screen', () => {
    expect(viewOf('/sign-in?returnTo=/dashboard')).toBeNull();
    expect(viewOf('/products/0199a1f0-0000')).toBeNull();
    expect(viewOf('/dashboard/somewhere')).toBeNull();
  });
});

describe('a screen opened', () => {
  it('counts a new route, not the same one with another query: a period or a filter is not an opening', () => {
    const telemetry = { trackEvent: vi.fn() };
    TestBed.configureTestingModule({ providers: [{ provide: Telemetry, useValue: telemetry }] });
    const usage = TestBed.inject(BackofficeUsage);

    usage.opened('/dashboard/sales?period=week');
    usage.opened('/dashboard/sales?period=day');
    usage.opened('/dashboard/operations?period=day');
    usage.opened('/orders');
    usage.opened('/orders?status=Confirmed');
    usage.opened('/dashboard/sales');

    expect(telemetry.trackEvent.mock.calls.map(([, attributes]) => attributes.view)).toEqual([
      'dashboard.sales', 'dashboard.operations', 'orders', 'dashboard.sales',
    ]);
  });
});

describe('how the server answered', () => {
  it('is refused for a 4xx and failed for anything else', () => {
    expect(outcomeOf(new HttpErrorResponse({ status: 409 }))).toBe('refused');
    expect(outcomeOf(new HttpErrorResponse({ status: 400 }))).toBe('refused');
    expect(outcomeOf(new HttpErrorResponse({ status: 503 }))).toBe('failed');
    expect(outcomeOf(new HttpErrorResponse({ status: 0 }))).toBe('failed');
    expect(outcomeOf(new Error('boom'))).toBe('failed');
  });
});
