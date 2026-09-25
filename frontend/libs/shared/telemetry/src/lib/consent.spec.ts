import { TestBed } from '@angular/core/testing';
import { AnalyticsConsent } from './consent';

describe('AnalyticsConsent', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('has asked nothing on a first visit', () => {
    expect(TestBed.inject(AnalyticsConsent).state()).toBe('unasked');
  });

  it('remembers the answer for the next visit', () => {
    TestBed.inject(AnalyticsConsent).deny();
    TestBed.resetTestingModule();

    expect(TestBed.inject(AnalyticsConsent).state()).toBe('denied');
  });

  it('asks again rather than assuming yes when storage cannot be read', () => {
    localStorage.setItem('tendero.analytics-consent', 'granted');
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(TestBed.inject(AnalyticsConsent).state()).toBe('unasked');
  });

  it('still takes the answer for this tab when storage cannot be written', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('quota');
    });
    const consent = TestBed.inject(AnalyticsConsent);

    consent.grant();

    expect(consent.state()).toBe('granted');
  });
});
