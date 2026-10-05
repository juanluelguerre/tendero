import { ApplicationRef } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TinoDoorStore } from './tino-door.store';

const Status = 'http://api/api/assistant/status';

/** Tino's door: whether its panel is open, and whether Tino can be asked at all. */
describe("tino's door", () => {
  let http: HttpTestingController;
  const telemetry = { trackEvent: vi.fn() };
  let door: InstanceType<typeof TinoDoorStore>;

  beforeEach(() => {
    telemetry.trackEvent.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        { provide: API_BASE_URL, useValue: 'http://api' },
        { provide: Telemetry, useValue: telemetry },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    door = TestBed.inject(TinoDoorStore);
  });

  afterEach(() => {
    // The status is read when the door is first drawn; a test that says nothing of it leaves it unanswered.
    http.match(Status);
    http.verify();
  });

  /** The server answers the one read the door makes, once the store's resource has asked. */
  async function says(...answer: Parameters<ReturnType<HttpTestingController['expectOne']>['flush']>) {
    TestBed.tick();
    http.expectOne(Status).flush(...answer);
    await TestBed.inject(ApplicationRef).whenStable();
  }

  /** The door is not drawn as "not available" while the shop has not answered: it would flicker on every visit. */
  it('says nothing about availability until the server has, and then what the server said', async () => {
    expect([door.available(), door.unavailable()]).toEqual([null, false]);

    await says({ available: true });

    expect([door.available(), door.unavailable()]).toEqual([true, false]);
  });

  it('is not available when the server says so', async () => {
    await says({ available: false });

    expect(door.unavailable()).toBe(true);
  });

  it('is not available when the server does not answer', async () => {
    await says('down', { status: 503, statusText: 'Service Unavailable' });

    expect([door.available(), door.unavailable()]).toEqual([false, true]);
  });

  it('opens and closes, and every opening is counted with where it came from and the page it was on', async () => {
    await TestBed.inject(Router).navigateByUrl('/es/p/sarten/PAN-1');

    door.toggle();
    expect(door.isOpen()).toBe(true);
    door.toggle();
    expect(door.isOpen()).toBe(false);
    door.open('header');
    door.open('header');

    // A closing is not an opening, and a door already open is not opened again.
    expect(telemetry.trackEvent.mock.calls).toEqual([
      ['tino_opened', { from: 'header', page: 'product' }],
      ['tino_opened', { from: 'header', page: 'product' }],
    ]);
  });

  /** Opening a door that leads nowhere is what the fallback's event is for too. */
  it('counts an opening while Tino is not available as one', async () => {
    await says({ available: false });

    door.toggle();

    expect(telemetry.trackEvent.mock.calls.map((call) => call[0])).toEqual(['tino_opened', 'tino_unavailable']);
    expect(telemetry.trackEvent).toHaveBeenLastCalledWith('tino_unavailable', { cause: 'not_available' });
  });

  it("opens from the search page's offer with the question to ask, which the panel takes once", async () => {
    await TestBed.inject(Router).navigateByUrl('/es?q=una%20sart%C3%A9n');

    door.ask('una sartén que no pese');

    expect([door.isOpen(), door.handed()]).toEqual([true, 'una sartén que no pese']);
    expect(telemetry.trackEvent).toHaveBeenCalledWith('tino_opened', { from: 'search', page: 'search' });

    door.taken();
    expect(door.handed()).toBeNull();
  });

  /** jsdom has no media queries to ask: the panel is then a sheet, never beside a page it cannot measure. */
  it('is docked only when it is open and there is room beside the page', () => {
    door.open('header');

    expect([door.wide(), door.docked()]).toEqual([false, false]);
  });
});
