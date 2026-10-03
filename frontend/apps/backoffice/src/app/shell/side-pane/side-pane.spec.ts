import { NgTemplateOutlet } from '@angular/common';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { SidePane, SidePaneContent } from './side-pane';

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
