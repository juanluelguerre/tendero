import { TestBed } from '@angular/core/testing';
import { StaffUsage } from './staff-usage';

/** The notice to staff (ADR 0041): on until switched off, seen once, remembered. */
describe('StaffUsage', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('starts on, with the notice not yet seen', () => {
    const staff = TestBed.inject(StaffUsage);

    expect([staff.state(), staff.noticeSeen()]).toEqual(['on', false]);
  });

  it('switching off counts as having seen the notice, and is remembered', () => {
    TestBed.inject(StaffUsage).switchOff();
    TestBed.resetTestingModule();

    const again = TestBed.inject(StaffUsage);
    expect([again.state(), again.noticeSeen()]).toEqual(['off', true]);
  });

  it('acknowledging leaves it on', () => {
    const staff = TestBed.inject(StaffUsage);
    staff.acknowledge();

    expect([staff.state(), staff.noticeSeen()]).toEqual(['on', true]);
  });
});
