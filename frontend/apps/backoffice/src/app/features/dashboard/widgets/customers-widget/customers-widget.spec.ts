import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CustomersWidget } from './customers-widget';
import { openWidget, setUpWidgets } from '../widget-testing';

const FUNNEL = (newCustomers: number, returningCustomers: number) => ({
  period: 'Week',
  cartsStarted: 10,
  checkedOut: 8,
  abandoned: 2,
  stillOpen: 0,
  conversion: 0.8,
  newCustomers,
  returningCustomers,
});

const open = (body: object) => openWidget(CustomersWidget, { '/api/dashboard/funnel': body });

describe('the customers widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  it('splits the buyers into new and returning, as figures and as one share', async () => {
    const element = await open(FUNNEL(6, 2));

    expect([...element.querySelectorAll('.fact__value')].map((value) => value.textContent?.trim())).toEqual(['6', '2']);
    expect([...element.querySelectorAll('table td')].map((cell) => cell.textContent?.trim())).toEqual(['6', '2']);
  });

  it('says nobody bought rather than drawing a share of nobody', async () => {
    const element = await open(FUNNEL(0, 0));

    expect(element.querySelector('backoffice-bar-chart')).toBeNull();
    expect(element.textContent).toContain('Nobody bought in this period.');
  });
});
