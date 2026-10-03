import { NgTemplateOutlet } from '@angular/common';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StewardDoorStore } from '../../features/steward/steward-door.store';
import { BackofficeUsage } from '../../usage';
import { SidePane, SidePaneContent } from './side-pane';

/** A window `rem` wide: a media query `(min-width: Nrem)` matches when N is not above it. */
function widthOf(rem: number): void {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: rem >= Number.parseFloat(/(\d+(?:\.\d+)?)rem/.exec(query)?.[1] ?? '0'),
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

beforeEach(() => {
  widthOf(70);
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: BackofficeUsage, useValue: { stewardOpened: () => undefined } }],
  });
});

afterEach(() => vi.restoreAllMocks());

/** What the shell does: draws the slot. */
@Component({
  imports: [NgTemplateOutlet],
  template: '<div id="slot"><ng-container *ngTemplateOutlet="pane.template()" /></div>',
})
class Slot {
  protected readonly pane = TestBed.inject(SidePane);
}

/** What a screen does: declares its pane where it lives, and opens and closes it with an @if. */
@Component({
  imports: [SidePaneContent],
  template: '@if (open()) { <p *backofficeSidePane>{{ owner }} pane</p> }',
})
class Screen {
  readonly open = signal(false);
  protected readonly owner = "The screen's";
}

describe('the side pane slot', () => {
  const draw = () => {
    const slot = TestBed.createComponent(Slot);
    const screen = TestBed.createComponent(Screen);
    const settle = () => {
      screen.detectChanges();
      slot.detectChanges();
    };
    return {
      slot,
      screen,
      settle,
      text: () => (slot.nativeElement as HTMLElement).querySelector('#slot')?.textContent?.trim(),
    };
  };

  it('draws a pane where the shell has its slot, from the screen that declared it, and takes it away when the screen does', () => {
    const { screen, settle, text } = draw();
    settle();
    expect(text()).toBe('');

    screen.componentInstance.open.set(true);
    settle();
    expect(text()).toBe("The screen's pane");
    expect(TestBed.inject(SidePane).isOpen()).toBe(true);

    screen.componentInstance.open.set(false);
    settle();
    expect(text()).toBe('');
    expect(TestBed.inject(SidePane).isOpen()).toBe(false);
  });

  it("does not let an earlier pane's end close a later one", () => {
    const slot = TestBed.inject(SidePane);
    const [first, second] = [{} as never, {} as never];

    slot.show(first);
    slot.show(second);
    slot.hide(first);

    expect(slot.template()).toBe(second);
  });
});

/**
 * Where the panes fit. Docked beside the page from 85 rem (the page keeps a narrow
 * table's 44 rem beside a 26 rem pane and the open rail); both docked from 112 rem;
 * between, the pane of an item and Steward take turns — the one opened last has the
 * room, and the other is out of the way, not closed; below 85 rem they are drawers over
 * the page and nothing takes turns.
 */
describe('the panes taking turns', () => {
  const [pane, other] = [{} as never, {} as never];

  it('closes Steward when a pane opens where there is room for one docked pane only', () => {
    widthOf(90);
    const door = TestBed.inject(StewardDoorStore);
    door.open();

    TestBed.inject(SidePane).show(pane);

    expect(door.isOpen()).toBe(false);
  });

  it('puts the pane out of the way, without closing it, when Steward is opened after it', () => {
    widthOf(90);
    const slot = TestBed.inject(SidePane);
    const door = TestBed.inject(StewardDoorStore);
    slot.show(pane);
    expect(slot.yields()).toBe(false);

    door.open();
    expect(slot.yields()).toBe(true);
    expect(slot.template()).toBe(pane);

    door.close();
    expect(slot.yields()).toBe(false);
  });

  it('opens a row of the list over Steward when the row is picked: the pane is brought back', () => {
    widthOf(90);
    const slot = TestBed.inject(SidePane);
    const door = TestBed.inject(StewardDoorStore);
    slot.show(pane);
    door.open();

    slot.reveal();

    expect(door.isOpen()).toBe(false);
  });

  it('keeps both docked where there is room for both', () => {
    widthOf(115);
    const slot = TestBed.inject(SidePane);
    const door = TestBed.inject(StewardDoorStore);
    door.open();

    slot.show(pane);

    expect([door.isOpen(), slot.yields()]).toEqual([true, false]);
  });

  it('takes no turns where the panes are drawers over the page', () => {
    widthOf(70);
    const slot = TestBed.inject(SidePane);
    const door = TestBed.inject(StewardDoorStore);
    door.open();

    slot.show(other);

    expect([door.isOpen(), slot.yields(), slot.docked()]).toEqual([true, false, false]);
  });
});
