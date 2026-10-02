import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Telemetry } from '@tendero/shared-telemetry';
import { describe, expect, it, vi } from 'vitest';
import { StewardDoorStore } from './steward-door.store';

/** Whether Steward's panel is open: the bar, the palette and the panel share it. */
describe('the steward door', () => {
  function door() {
    const telemetry = { trackEvent: vi.fn() };
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Telemetry, useValue: telemetry }] });
    return { door: TestBed.inject(StewardDoorStore), telemetry };
  }

  it('opens, closes and toggles', () => {
    const { door: steward } = door();
    steward.open();
    const opened = steward.isOpen();
    steward.toggle();
    const toggled = steward.isOpen();
    steward.toggle();

    expect([opened, toggled, steward.isOpen()]).toEqual([true, false, true]);
  });

  /** Opening is counted with the screen it happened on; closing is not an opening. */
  it('reports each opening with its screen, and only openings', async () => {
    const { door: steward, telemetry } = door();
    await TestBed.inject(Router).navigateByUrl('/');
    vi.spyOn(TestBed.inject(Router), 'url', 'get').mockReturnValue('/orders?status=Confirmed');
    steward.open();
    steward.close();
    steward.toggle();

    expect(telemetry.trackEvent.mock.calls).toEqual([
      ['steward_opened', { view: 'orders' }],
      ['steward_opened', { view: 'orders' }],
    ]);
  });
});
