import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ToggleSwitch } from './toggle-switch';

@Component({
  imports: [ToggleSwitch],
  template: `<lib-toggle-switch [checked]="on()" [disabled]="off()" (toggled)="said.push($event); on.set($event)"
    >Offers and new arrivals</lib-toggle-switch
  >`,
})
class Host {
  readonly on = signal(false);
  readonly off = signal(false);
  readonly said: boolean[] = [];
}

/**
 * A yes-or-no fact as a switch, one control for every page that has one (the
 * filter panel's, the trail's, the kit's requirements, the account's preferences):
 * a button that says it is a switch and whether it is on, its label inside it, so
 * the whole row is the target. What switching does is the page's.
 */
describe('the toggle switch', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;
    return { fixture, button, host: fixture.componentInstance };
  }

  it('is a switch named by its label, and says whether it is on', () => {
    const { fixture, button, host } = render();

    expect([button.getAttribute('role'), button.getAttribute('aria-checked'), button.textContent?.trim()]).toEqual([
      'switch',
      'false',
      'Offers and new arrivals',
    ]);

    host.on.set(true);
    fixture.detectChanges();
    expect(button.getAttribute('aria-checked')).toBe('true');
  });

  /** It says what the shopper asked for; the page decides, and tells it back. */
  it('asks for the other value when pressed', () => {
    const { fixture, button, host } = render();

    button.click();
    fixture.detectChanges();
    button.click();

    expect(host.said).toEqual([true, false]);
  });

  it('asks for nothing while it is disabled', () => {
    const { fixture, button, host } = render();
    host.off.set(true);
    fixture.detectChanges();

    button.click();

    expect([button.disabled, host.said]).toEqual([true, []]);
  });

  it('is a button that submits nothing', () => {
    expect(render().button.type).toBe('button');
  });
});
