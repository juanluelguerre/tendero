import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ConversationScroll } from './conversation-scroll';

@Component({
  imports: [ConversationScroll],
  template: `
    <div [libConversationScroll]="questions().length">
      @for (question of questions(); track $index) {
        <p data-question>{{ question }}</p>
      }
    </div>
  `,
})
class Host {
  readonly questions = signal<string[]>([]);
}

/** Where a conversation scrolls: the newest question at the top of the view (phase 12, 12b). */
describe('the conversation scroll', () => {
  function drawn() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const element = (fixture.nativeElement as HTMLElement).querySelector('div')!;
    // jsdom lays nothing out: the view is at 0 and each question is 100 px under the one before.
    element.getBoundingClientRect = () => ({ top: 0 }) as DOMRect;
    const ask = async (question: string) => {
      fixture.componentInstance.questions.update((all) => [...all, question]);
      fixture.detectChanges();
      element.querySelectorAll('[data-question]').forEach((node, index) => {
        node.getBoundingClientRect = () => ({ top: index * 100 - element.scrollTop }) as DOMRect;
      });
      await Promise.resolve();
    };
    return { element, ask };
  }

  it('does not move before anything is asked', async () => {
    const { element } = drawn();
    await Promise.resolve();

    expect(element.scrollTop).toBe(0);
  });

  it('brings each new question to the top of the view', async () => {
    const { element, ask } = drawn();

    await ask('una sartén');
    expect(element.scrollTop).toBe(0);
    await ask('¿y para el horno?');
    expect(element.scrollTop).toBe(100);
    await ask('¿cuánto pesa?');
    expect(element.scrollTop).toBe(200);
  });
});
