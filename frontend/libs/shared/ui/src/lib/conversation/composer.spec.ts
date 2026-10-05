import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Composer } from './composer';

@Component({
  imports: [Composer],
  template: `
    <form (submit)="$event.preventDefault(); box.send()">
      <textarea libComposer #box="libComposer" [busy]="busy()" (asked)="asked.push($event)"></textarea>
      <button type="submit" [disabled]="!box.ready()">send</button>
    </form>
  `,
})
class Host {
  readonly busy = signal(false);
  readonly asked: string[] = [];
}

/** The box a question to an agent is typed in: Steward's and Tino's (phase 12b). */
describe('the composer', () => {
  function drawn() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const box = element.querySelector('textarea')!;
    const type = (text: string) => {
      box.value = text;
      box.dispatchEvent(new Event('input'));
    };
    const enter = (shiftKey = false) => box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey }));
    return { fixture, host: fixture.componentInstance, box, button: element.querySelector('button')!, type, enter };
  }

  it('sends what was typed with Enter, and leaves the box empty', () => {
    const { fixture, host, box, button, type, enter } = drawn();
    expect(button.disabled).toBe(true);

    type('una sartén');
    fixture.detectChanges();
    expect(button.disabled).toBe(false);
    enter();
    fixture.detectChanges();

    expect(host.asked).toEqual(['una sartén']);
    expect([box.value, button.disabled]).toEqual(['', true]);
  });

  /** A slow frame, or a tab in the background: nothing was drawn between the last key and Enter. */
  it('leaves the box empty when Enter comes before anything was drawn', () => {
    const { host, box, type, enter } = drawn();

    type('una sartén');
    enter();

    expect(host.asked).toEqual(['una sartén']);
    expect(box.value).toBe('');
  });

  it('adds a line with Shift+Enter, and sends nothing that is only spaces', () => {
    const { host, type, enter } = drawn();

    type('una sartén');
    enter(true);
    type('   ');
    enter();

    expect(host.asked).toEqual([]);
  });

  it('sends nothing while a turn is on its way, and keeps what was typed', () => {
    const { fixture, host, box, button, type, enter } = drawn();
    host.busy.set(true);
    fixture.detectChanges();

    type('otra pregunta');
    fixture.detectChanges();
    enter();

    expect(host.asked).toEqual([]);
    expect([box.value, button.disabled]).toEqual(['otra pregunta', true]);
  });

  it('has the focus when it is drawn', async () => {
    const fixture = TestBed.createComponent(Host);
    document.body.append(fixture.nativeElement);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.activeElement).toBe((fixture.nativeElement as HTMLElement).querySelector('textarea'));
    (fixture.nativeElement as HTMLElement).remove();
  });
});
