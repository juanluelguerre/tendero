import { Directive, effect, ElementRef, inject, input } from '@angular/core';

/**
 * On the element a conversation scrolls in: the newest question — the last
 * `[data-question]` — goes to the top of the view as it is asked, so its answer,
 * and anything that loads after it, grows below it in sight. Scrolling to the
 * bottom instead left what arrived later under the fold.
 *
 * The conversation's own scroll, never `scrollIntoView`: that scrolls every
 * ancestor, the page included.
 */
@Directive({ selector: '[libConversationScroll]' })
export class ConversationScroll {
  /** How many questions have been asked: each new one moves the view. */
  readonly asked = input.required<number>({ alias: 'libConversationScroll' });

  constructor() {
    const element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    effect(() => {
      if (this.asked() === 0) return;
      // After the render that draws the question.
      queueMicrotask(() => {
        const questions = element.querySelectorAll('[data-question]');
        const last = questions[questions.length - 1];
        if (!last) return;
        const padding = Number.parseFloat(getComputedStyle(element).paddingTop) || 0;
        element.scrollTop += last.getBoundingClientRect().top - element.getBoundingClientRect().top - padding;
      });
    });
  }
}
