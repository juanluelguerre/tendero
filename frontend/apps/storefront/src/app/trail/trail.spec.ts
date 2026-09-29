import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { BrowsingHistoryView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { Telemetry } from '@tendero/shared-telemetry';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BrowsingHistoryService } from '../data-access/browsing-history.service';
import { CustomerLink } from '../data-access/customer-link';
import { Trail } from './trail';
import { TrailStorageKey } from './trail-storage';

/**
 * Which trail is in charge (R-8, H-3/H-3b): the browser's for a guest, the
 * account's for a customer, one merged into the other at sign-in. The rules
 * themselves are `trail-rules.spec.ts`'s; this is the wiring.
 */
describe('the trail', () => {
  const signedIn = signal(false);
  let events: [string, unknown][];
  let calls: string[];
  let linked: number;

  const account = (views: string[], searches: string[] = [], enabled = true): BrowsingHistoryView => ({
    enabled,
    views: views.map((code) => ({ code, at: '2026-09-29T10:00:00Z' })),
    searches: searches.map((text) => ({ text, at: '2026-09-29T10:00:00Z' })),
  });

  beforeEach(() => {
    signedIn.set(false);
    events = [];
    calls = [];
    linked = 0;
    localStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthStore, useValue: { isSignedIn: signedIn } as unknown as AuthStore },
        { provide: Telemetry, useValue: { trackEvent: (name: string, a: unknown) => events.push([name, a]) } as unknown as Telemetry },
        { provide: CustomerLink, useValue: { ensure: () => (linked++, Promise.resolve()) } as unknown as CustomerLink },
        {
          provide: BrowsingHistoryService,
          useValue: {
            get: () => (calls.push('get'), of(account(['FROMACCOUNT']))),
            merge: (trail: { views: { code: string }[] }) => (
              calls.push(`merge:${trail.views.map((v) => v.code).join(',')}`), of(account(trail.views.map((v) => v.code)))
            ),
            recordView: (code: string) => (calls.push(`view:${code}`), of(account([code]))),
            recordSearch: (text: string) => (calls.push(`search:${text}`), of(account([], [text]))),
            forget: (kind: string, key: string) => (calls.push(`forget:${kind}:${key}`), of(account([]))),
            clear: () => (calls.push('clear'), of(account([]))),
            setEnabled: (enabled: boolean) => (calls.push(`enabled:${enabled}`), of(account([], [], enabled))),
          } as unknown as BrowsingHistoryService,
        },
      ],
    });
  });

  afterEach(() => localStorage.clear());

  it('keeps a guest trail in this browser only, most recent first', () => {
    const trail = TestBed.inject(Trail);

    trail.recordView('AAA');
    trail.recordSearch('mochila');
    trail.recordView('BBB');

    expect(trail.shown().views.map((entry) => entry.key)).toEqual(['BBB', 'AAA']);
    expect(JSON.parse(localStorage.getItem(TrailStorageKey) ?? '{}').views).toHaveLength(2);
    expect(calls).toEqual([]);
  });

  it('shows nothing, and throws nothing, when storage is blocked', () => {
    const blocked = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    const writing = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });

    const trail = TestBed.inject(Trail);
    expect(trail.any()).toBe(false);
    expect(() => trail.recordView('AAA')).not.toThrow();

    blocked.mockRestore();
    writing.mockRestore();
  });

  it('counts a removal and a clearing, and forgets what it says', () => {
    const trail = TestBed.inject(Trail);
    trail.recordView('AAA');
    trail.recordView('BBB');

    trail.forget('View', 'AAA');
    expect(trail.kept().views.map((entry) => entry.key)).toEqual(['BBB']);

    trail.clear();
    expect(trail.any()).toBe(false);
    expect(events).toEqual([
      ['trail_cleared', { scope: 'one' }],
      ['trail_cleared', { scope: 'all' }],
    ]);
  });

  it('records nothing once switched off, and remembers the switch', () => {
    const trail = TestBed.inject(Trail);
    trail.recordView('AAA');

    trail.setEnabled(false);
    trail.recordView('BBB');

    expect(trail.kept()).toEqual({ enabled: false, views: [], searches: [] });
    expect(JSON.parse(localStorage.getItem(TrailStorageKey) ?? '{}').enabled).toBe(false);
  });

  it('merges the guest trail once at sign-in, after linking the customer, and clears it here', async () => {
    const trail = TestBed.inject(Trail);
    trail.recordView('AAA');

    signedIn.set(true);
    TestBed.tick();
    await vi.waitFor(() => expect(calls).toEqual(['merge:AAA']));

    expect(linked).toBeGreaterThan(0);
    expect(trail.kept().views.map((entry) => entry.key)).toEqual(['AAA']);
    await vi.waitFor(() => expect(JSON.parse(localStorage.getItem(TrailStorageKey) ?? '{}').views).toEqual([]));
  });

  it('reads the account trail when there is nothing to merge, and records there', async () => {
    const trail = TestBed.inject(Trail);
    signedIn.set(true);
    TestBed.tick();
    await vi.waitFor(() => expect(trail.kept().views.map((entry) => entry.key)).toEqual(['FROMACCOUNT']));

    trail.recordView('CCC');
    await vi.waitFor(() => expect(calls).toEqual(['get', 'view:CCC']));
    expect(localStorage.getItem(TrailStorageKey)).toBeNull();
  });
});
