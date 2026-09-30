import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { orderStatusTone, returnStatusTone, StatusPill } from './status-pill';

/**
 * One status, one tone, in both apps (the author, 2026-09-30). The two apps used
 * to disagree about an order in transit: neutral in the backoffice, clay in the
 * storefront — and clay is the accent design/DESIGN.md keeps for what an agent
 * did, and it read almost as the danger red beside *Cancelado*.
 */
describe('a status', () => {
  it('has one tone per order status, and an order in transit is neutral', () => {
    expect(orderStatusTone('Pending')).toBe('muted');
    expect(orderStatusTone('PaymentAuthorized')).toBe('warn');
    expect(orderStatusTone('Confirmed')).toBe('warn');
    expect(orderStatusTone('Shipped')).toBe('neutral');
    expect(orderStatusTone('Delivered')).toBe('good');
    expect(orderStatusTone('PaymentFailed')).toBe('bad');
    expect(orderStatusTone('Cancelled')).toBe('bad');
  });

  it('has one tone per return status, and a return received is neutral', () => {
    expect(returnStatusTone('Requested')).toBe('muted');
    expect(returnStatusTone('Approved')).toBe('warn');
    expect(returnStatusTone('Received')).toBe('neutral');
    expect(returnStatusTone('Refunded')).toBe('good');
    expect(returnStatusTone('Rejected')).toBe('bad');
    expect(returnStatusTone('Cancelled')).toBe('bad');
  });

  @Component({ imports: [StatusPill], template: `<lib-status-pill tone="good">Entregado</lib-status-pill><lib-status-pill class="big" tone="bad" size="md">Cancelado</lib-status-pill>` })
  class Host {}

  it('is the word in a pill of its tone, at the size its place wants', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const [small, big] = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('lib-status-pill > span')) as HTMLElement[];

    expect([small.textContent?.trim(), small.dataset['tone'], small.dataset['size']]).toEqual(['Entregado', 'good', 'sm']);
    expect([big.dataset['tone'], big.dataset['size']]).toEqual(['bad', 'md']);
  });
});
