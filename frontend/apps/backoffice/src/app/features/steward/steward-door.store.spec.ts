import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { StewardDoorStore } from './steward-door.store';

/** Whether Steward's panel is open: the bar, the palette and the panel share it. */
describe('the steward door', () => {
  it('opens, closes and toggles', () => {
    const door = TestBed.inject(StewardDoorStore);
    door.open();
    const opened = door.isOpen();
    door.toggle();
    const toggled = door.isOpen();
    door.toggle();

    expect([opened, toggled, door.isOpen()]).toEqual([true, false, true]);
  });
});
