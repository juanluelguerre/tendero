import { NgTemplateOutlet } from '@angular/common';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
 * Where the panes sit. Steward is docked beside the page from 85 rem, which is where
 * the page keeps a narrow table's 44 rem beside a 26 rem panel and the open rail. The
 * pane of a list's item is a drawer over the page — the list keeps all its width, and
 * Steward and the pane can both be open at any width — and docks only from 112 rem,
 * where there is room to spare for both.
 */
describe('where the panes sit', () => {
  it("has Steward docked and the item's pane a drawer between 85 and 112 rem", () => {
    widthOf(90);
    const slot = TestBed.inject(SidePane);

    expect([slot.stewardDocked(), slot.docked()]).toEqual([true, false]);
  });

  it('docks both where there is room to spare', () => {
    widthOf(115);
    const slot = TestBed.inject(SidePane);

    expect([slot.stewardDocked(), slot.docked()]).toEqual([true, true]);
  });

  it('has drawers and sheets, and nothing docked, below 85 rem', () => {
    widthOf(70);
    const slot = TestBed.inject(SidePane);

    expect([slot.stewardDocked(), slot.docked()]).toEqual([false, false]);
  });
});
