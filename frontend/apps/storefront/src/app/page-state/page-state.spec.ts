import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { PageState, type PageStateTone } from './page-state';

@Component({
  imports: [PageState],
  template: `<storefront-page-state [heading]="heading()" [tone]="tone()" [level]="level()"
    ><p>Try fewer words.</p></storefront-page-state
  >`,
})
class Host {
  readonly heading = signal<string | undefined>('Nothing matches «mochila»');
  readonly tone = signal<PageStateTone>('info');
  readonly level = signal<1 | 2>(2);
}

/**
 * What a page shows when it has nothing else to (ADR 0049): a title at the
 * page's level, the caller's words, and a tone that tells "not here" from "ask
 * me again".
 */
describe('a page state', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const element = (fixture: ReturnType<typeof render>) => fixture.nativeElement as HTMLElement;

  it('names the state at the level it is given, above the words it was given', () => {
    const fixture = render();
    expect(element(fixture).querySelector('h2')?.textContent?.trim()).toBe('Nothing matches «mochila»');
    expect(element(fixture).querySelector('h2 + p')?.textContent).toBe('Try fewer words.');

    fixture.componentInstance.level.set(1);
    fixture.detectChanges();
    expect(element(fixture).querySelector('h2')).toBeNull();
    expect(element(fixture).querySelector('h1')?.textContent?.trim()).toBe('Nothing matches «mochila»');
  });

  it('draws no heading when it has no title', () => {
    const fixture = render();
    fixture.componentInstance.heading.set(undefined);
    fixture.detectChanges();
    expect(element(fixture).querySelector('h1, h2')).toBeNull();
  });

  it('tells a failure from an empty answer', () => {
    const fixture = render();
    const box = () => element(fixture).querySelector('storefront-page-state > div') as HTMLElement;
    expect(box().dataset['tone']).toBe('info');

    fixture.componentInstance.tone.set('bad');
    fixture.detectChanges();
    expect(box().dataset['tone']).toBe('bad');
  });
});
