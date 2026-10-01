import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Notice, type NoticeTone } from './notice';

@Component({
  imports: [Notice],
  template: `<lib-notice [tone]="tone()" [live]="live()">The search engine did not answer.</lib-notice>`,
})
class Host {
  readonly tone = signal<NoticeTone>('bad');
  readonly live = signal<'polite' | 'assertive' | null>('assertive');
}

/**
 * Something the reader has to read before carrying on, in one shape for both
 * apps (the inventory of 2026-09-30 found six `.notice`s, two of them unstyled).
 * The tone is a colour AND a word the caller wrote — colour never carries meaning
 * alone (design/DESIGN.md).
 */
describe('a notice', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const box = (fixture: ReturnType<typeof render>) =>
    (fixture.nativeElement as HTMLElement).querySelector('lib-notice > div') as HTMLElement;

  it('says what it was given, in its tone', () => {
    const fixture = render();

    expect(box(fixture).textContent?.trim()).toBe('The search engine did not answer.');
    expect(box(fixture).dataset['tone']).toBe('bad');
  });

  it('interrupts only when asked to: alert, status, or nothing', () => {
    const fixture = render();
    expect(box(fixture).getAttribute('role')).toBe('alert');

    fixture.componentInstance.live.set('polite');
    fixture.detectChanges();
    expect(box(fixture).getAttribute('role')).toBe('status');

    fixture.componentInstance.live.set(null);
    fixture.detectChanges();
    expect(box(fixture).hasAttribute('role')).toBe(false);
  });
});
