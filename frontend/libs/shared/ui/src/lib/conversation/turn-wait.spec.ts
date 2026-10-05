import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TurnWait } from './turn-wait';

@Component({
  imports: [TurnWait],
  template: `<lib-turn-wait working="Working" [slow]="slow" />`,
})
class Host {
  slow: string | undefined = 'A local model can take a minute or more.';
}

/** The wait counts the seconds, and says it can last once it is past the hosted models' time. */
describe('the turn wait', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const shown = (slow = true) => {
    const fixture = TestBed.createComponent(Host);
    if (!slow) fixture.componentInstance.slow = undefined;
    fixture.detectChanges();
    return async (seconds: number) => {
      await vi.advanceTimersByTimeAsync(seconds * 1000);
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).textContent ?? '';
    };
  };

  it('says what is being done and counts the seconds from zero', async () => {
    const text = shown();

    expect(await text(0)).toMatch(/Working\s+0 s/);
    expect(await text(7)).toContain('7 s');
  });

  it('says it can take longer only once the wait is long', async () => {
    const text = shown();

    expect(await text(14)).not.toContain('local model');
    expect(await text(1)).toContain('local model');
  });

  it('says nothing more, however long, when its caller has no words for it', async () => {
    const text = shown(false);

    expect(await text(40)).toMatch(/^\s*Working\s+40 s\s*$/);
  });
});
