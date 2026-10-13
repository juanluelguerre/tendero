import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ReturnsWidget } from './returns-widget';
import { openWidget, setUpWidgets } from '../widget-testing';

/** Money as the API sends it: minor units and its currency. */
const eur = (amount: number) => ({ amount: Math.round(amount * 100), currency: 'EUR' });

const OPS = {
  period: 'Week',
  toShip: 0,
  lateToShip: 0,
  inProgress: 0,
  paymentFailed: 0,
  cancelled: 0,
  cancellationRate: 0,
  cancellationTarget: 0.02,
  cancellationStanding: 'within',
  openReturns: 4,
  openReturnsByReason: [
    { reason: 'Damaged', count: 3 },
    { reason: 'WrongSize', count: 1 },
  ],
  refunded: eur(80),
  returnRate: 0.05,
};

const open = (body: object) => openWidget(ReturnsWidget, { '/api/dashboard/operations': body });

describe('the returns widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  /** The reasons' words are the returns screen's own (P11-7): one translation of five words, not two that drift. */
  it('ranks the open returns by reason in the returns screen words', async () => {
    const element = await open(OPS);

    expect([...element.querySelectorAll('table tbody th')].map((cell) => cell.textContent?.trim())).toEqual([
      'Damaged',
      'Wrong size',
    ]);
    expect([...element.querySelectorAll('table td')].map((cell) => cell.textContent?.trim())).toEqual(['3', '1']);
  });

  it('says there is nothing open rather than drawing no bars', async () => {
    const element = await open({ ...OPS, openReturns: 0, openReturnsByReason: [] });

    expect(element.querySelector('backoffice-bar-chart')).toBeNull();
    expect(element.textContent).toContain('No returns open.');
  });
});
