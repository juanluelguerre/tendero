import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Sparkline } from './sparkline';

function render() {
  const fixture = TestBed.createComponent(Sparkline);
  fixture.componentRef.setInput('values', [1, 2, 3]);
  fixture.detectChanges();
  return fixture;
}

describe('the sparkline component', () => {
  it('is hidden from a screen reader, whose figures are the number beside it', () => {
    expect((render().nativeElement as HTMLElement).getAttribute('aria-hidden')).toBe('true');
  });

  it('draws with ECharts, deferred until it is on screen', async () => {
    expect(await render().getDeferBlocks()).toHaveLength(1);
  });
});
