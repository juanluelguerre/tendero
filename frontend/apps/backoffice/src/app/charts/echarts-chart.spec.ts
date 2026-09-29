import { TestBed } from '@angular/core/testing';
import { ThemeStore } from '@tendero/shared-ui';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ChartBuild } from './echarts-chart';
import { EchartsChart } from './echarts-chart';

const motion = (matches: boolean) => () => ({ matches, addEventListener: () => undefined, removeEventListener: () => undefined });

describe('the ECharts host', () => {
  // jsdom has no matchMedia; the host asks it whether the reader wants motion.
  vi.stubGlobal('matchMedia', motion(false));
  afterEach(() => {
    vi.stubGlobal('matchMedia', motion(false));
    TestBed.inject(ThemeStore).choose('system');
  });

  // The directive itself (ECharts in a DOM with no layout) is not what is tested here.
  beforeEach(() => TestBed.overrideComponent(EchartsChart, { set: { imports: [], template: '' } }));

  function render(build: ChartBuild) {
    const fixture = TestBed.createComponent(EchartsChart);
    fixture.componentRef.setInput('build', build);
    return fixture;
  }

  const optionsOf = (fixture: ReturnType<typeof render>) => (fixture.componentInstance as unknown as { options: () => unknown }).options();

  it('builds its options from the colours of the tokens where it sits', () => {
    const fixture = render((colours) => ({ colour: colours.series.data }));
    (fixture.nativeElement as HTMLElement).style.setProperty('--chart-data', 'rgb(42, 105, 176)');
    fixture.detectChanges();

    expect(optionsOf(fixture)).toEqual({ colour: 'rgb(42, 105, 176)' });
  });

  /** Colours read in JavaScript follow a theme switch only if the options are rebuilt on it. */
  it('reads the tokens again when the theme changes', () => {
    const theme = TestBed.inject(ThemeStore);
    theme.choose('light');
    const fixture = render((colours) => ({ colour: colours.series.data }));
    const host = fixture.nativeElement as HTMLElement;
    host.style.setProperty('--chart-data', 'rgb(42, 105, 176)');
    fixture.detectChanges();
    expect(optionsOf(fixture)).toEqual({ colour: 'rgb(42, 105, 176)' });

    host.style.setProperty('--chart-data', 'rgb(94, 151, 207)');
    theme.choose('dark');
    fixture.detectChanges();

    expect(optionsOf(fixture)).toEqual({ colour: 'rgb(94, 151, 207)' });
  });

  it('tells the build whether the reader asked for less motion', () => {
    vi.stubGlobal('matchMedia', motion(true));
    const fixture = render((_, reducedMotion) => ({ reducedMotion }));
    fixture.detectChanges();

    expect(optionsOf(fixture)).toEqual({ reducedMotion: true });
  });
});
