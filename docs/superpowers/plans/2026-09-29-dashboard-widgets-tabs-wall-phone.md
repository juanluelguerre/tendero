# Dashboard widgets, tabs, the wall and the phone — Implementation Plan (R-9, Plan 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the one-page dashboard into option B — an Overview and the tabs Sales, Operations, Growth and Agents, each with its URL and the period in it — built from widgets that honour a size contract (S, M, L), each tab able to go full screen on a monitor with a minute's refresh, and all of it usable on a phone.

**Architecture:** Three layers, as the spec decided. **Charts** only draw: one generic ECharts host takes a *build* function (colours, reduced motion → options), and `BarChart` (columns, rows, share) and `Sparkline` are contract components in front of it. **Widgets** read their data through one `DashboardSources` service (so the Overview's six widgets ask each endpoint once) and a `DashboardRefresh` clock, and choose what to show from their own width with container queries. **Dashboards** are data: a list of placements (widget + size) per tab, rendered by `DashboardGrid` from a registry of widget components, so v2's configurable dashboard is a change of data, not of code.

**Tech Stack:** Angular 22 (signals, `rxResource`, `@defer`, `NgComponentOutlet` with inputs, container queries), Transloco, ECharts 6.1.0 through our own directive, Vitest 4, Playwright against the running stack.

**Spec:** `docs/specs/backoffice-dashboard.md` — *Behavior* (Overview, tabs, full screen, size contract, phone, accessibility), *The data*, and *Building it, in order* (D-3 to D-6). Plan 1 (`docs/superpowers/plans/2026-09-29-dashboard-series-and-chart-engines.md`) built D-1 and D-2.

## Global Constraints

- Only data that exists today. D-7 (Plan 3) brings the new data: category, surface, coverage, campaign baseline, defect and late rates, sell-through, units per order, search status. A widget that needs it is **not** built here, and no widget shows a figure it does not have.
- Every figure computed on the server by the context that owns it; the browser only says it (`figures.ts` is presentation only).
- Colours come only from `design/tokens.css`, through `readChartColours` for ECharts and `var()` for CSS; never a hex in a component. Agents are clay (`--chart-agent`).
- Every user-facing string in es AND en, in `apps/backoffice/public/i18n/{en,es}.json`; insert new blocks by hand beside their siblings — never re-sort the files (Plan 1 found that a sort rewrites 141 lines).
- An Angular component is three files (`.ts`, `.html`, `.css`).
- No new npm dependency.
- `@defer (on viewport)` keeps ECharts out of the initial bundle; nothing in `main` may import a runtime value from `echarts`.
- Every chart has its figures beside it (spec, *Accessibility*): a visible number at S, a table at L, a toggle at M.
- `prefers-reduced-motion` turns animation off.
- The period in the URL is `?period=day|week|month`; the spec's example is lowercase. Read it in any case, write it lowercase, and treat anything else as `week`.
- Full screen uses the browser's Fullscreen API on the tab's element: no kiosk mode, no second identity, no token in a URL.
- Commits: Conventional Commits, English, **no attribution trailers** (CLAUDE.md).
- Frontend checks: `cd frontend && npx nx run-many -t lint test -p backoffice shared-i18n --skip-nx-cache`, plus `npx nx build backoffice` with no new warning.

## Review Focus

1. **Stale or odd URLs**: `?period=YEAR`, `?period=` or `?period=WEEK` must give Week, Week and Week, and the control must show it pressed. A link from another screen with no query must also give Week. → Task 8, `dashboard-period.spec.ts`.
2. **One source fails while the others answer**: only that widget says it failed. A failure must not stay cached: the next refresh asks again. → Task 5, `dashboard-sources.spec.ts`.
3. **Leaving full screen with Esc**, not the button: the refresh stops and the page's own head comes back. → Task 9, `dashboard-page.spec.ts` ("leaves the wall when the browser does").
4. **Two widgets on the same endpoint** (key figures and best sellers both read `/sales`): one request, not two. → Task 5, `dashboard-sources.spec.ts`.
5. **A period with nothing sold**: the channel share has no whole to divide. It must say so rather than draw an empty or `NaN` bar. → Task 5, `by-channel-widget.spec.ts`.

---

## File map

```
frontend/apps/backoffice/src/app/charts/
  echarts-chart.ts/.html/.css         (renamed from echarts-bar-chart.*; generic host, takes a ChartBuild)   T1
  echarts-chart.spec.ts               (renamed from echarts-bar-chart.spec.ts)                               T1
  echarts.directive.ts                (registers LineChart too)                                              T3
  echarts-bar-options.ts/.spec.ts     (layout: columns | rows | share)                                       T2
  bar-chart.ts/.html/.css/.spec.ts    (layout input; table visible at L)                                     T1, T2, T4
  sparkline.ts/.html/.css/.spec.ts    (new)                                                                  T3
  echarts-sparkline-options.ts/.spec.ts (new)                                                              T3
frontend/apps/backoffice/src/app/features/dashboard/
  dashboard-widgets.ts                (WidgetSize, WidgetKey, WidgetPlacement, DASHBOARD_WIDGETS)            T4
  dashboard-grid.ts/.html/.css/.spec.ts (new)                                                              T4
  dashboard-sources.ts/.spec.ts       (new: shared, de-duplicated reads)                                     T5
  dashboard-refresh.ts/.spec.ts       (new: the clock; the minute's tick in T9)                              T5, T9
  key-figures.ts/.spec.ts             (new, pure)                                                            T5
  key-figures-widget.*                (new, L)                                                               T5
  best-sellers-widget.*               (new)                                                                  T5
  by-channel-widget.*  (+ .spec)      (new)                                                                  T5
  sales-by-day-widget.*               (reads through DashboardSources)                                       T5
  orders-by-status-widget.*           (new)                                                                  T6
  health-widget.*                     (new, S)                                                               T6
  returns-widget.*                    (new)                                                                  T6
  stock-widget.*                      (new)                                                                  T6
  funnel-widget.*                     (loses "Buyers"; reads through sources)                                T6, T7
  tasks-widget.*                      (reads stock through sources)                                          T6
  customers-widget.*                  (new)                                                                  T7
  agent-sales-widget.*                (new, from agents-widget's first half)                                 T7
  refusals-widget.*                   (new, from agents-widget's second half)                                T7
  sales-widget.*, operations-widget.*, agents-widget.*   (deleted once split)                                T5–T7
  dashboard-period.ts/.spec.ts        (new, pure)                                                            T8
  dashboard-layouts.ts/.spec.ts       (new: the five tabs as data)                                           T8
  dashboard-widget-registry.ts        (new: WidgetKey → component)                                           T8
  dashboard-page.ts/.html/.css/.spec.ts (the shell: tabs, period, full screen, outlet)                       T8, T9
  dashboard-tab-page.ts/.html/.css    (new: one tab = one grid)                                              T8
  full-screen.ts/.spec.ts             (new: the Fullscreen API behind a signal)                              T9
frontend/apps/backoffice/src/app/app.routes.ts                                                               T8
frontend/apps/backoffice/src/styles.css      (size-contract classes; the wall's type scale)                  T4, T9
frontend/apps/backoffice/public/i18n/{en,es}.json                                                            T5–T9
frontend/apps/backoffice-e2e/src/dashboard.spec.ts (new), accessibility.spec.ts                              T10
docs/specs/backoffice-dashboard.md, docs/delivery-plan.md, docs/blog/notebook.md, docs/book/material-map.md  T10
```

**The size contract, as numbers** (Task 4, used by every widget). A widget's cell is a container named `widget`. From **20rem** it shows its M content, and from **56rem** its L content. At 1440 with the sidebar, S ≈ 17rem, M ≈ 35rem and L ≈ 72rem. On a phone every cell is ≈ 22rem, so it shows M. On a 1920 wall the root type grows to ≈ 20px (Task 9), which gives S ≈ 23rem (the chart) and M ≈ 47rem (still M). Because the thresholds are in `rem`, the wall's larger type moves them with it. The executor checks these against screenshots in Task 10 and records any change as a Ruling.

---

### Task 1: A generic ECharts host

`EchartsBarChart` knows about bars. Every chart after it (the sparkline, the row and share bars) needs the same theme-following, token-reading host. Only the options differ. The host becomes generic and takes a *build* function.

**Files:**
- Rename: `charts/echarts-bar-chart.{ts,html,css}` → `charts/echarts-chart.{ts,html,css}`, `echarts-bar-chart.spec.ts` → `echarts-chart.spec.ts` (`git mv`)
- Modify: `charts/bar-chart.ts`, `charts/bar-chart.html`, `charts/bar-chart.css`

**Interfaces:**
- Consumes: `ChartColours`, `readChartColours(element)` (`chart-colours.ts`), `ThemeStore.applied` (`@tendero/shared-ui`), `EchartsDirective` (`[backofficeEcharts]`).
- Produces: `export type ChartBuild = (colours: ChartColours, reducedMotion: boolean) => EChartsCoreOption;` and component `EchartsChart` (selector `backoffice-echarts-chart`, input `build: ChartBuild`). The plot's height is the CSS custom property `--chart-block-size` (default `12.5rem`), which BarChart's placeholder uses too.

- [ ] **Step 1: Write the failing test** — replace the content of the renamed `echarts-chart.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { ThemeStore } from '@tendero/shared-ui';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ChartBuild } from './echarts-chart';
import { EchartsChart } from './echarts-chart';

describe('the ECharts host', () => {
  // jsdom has no matchMedia; the host asks it whether the reader wants motion.
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: () => undefined, removeEventListener: () => undefined }));
  afterEach(() => TestBed.inject(ThemeStore).choose('system'));

  function render(build: ChartBuild) {
    // The directive itself (ECharts in a DOM with no layout) is not what is tested here.
    TestBed.overrideComponent(EchartsChart, { set: { imports: [], template: '' } });
    const fixture = TestBed.createComponent(EchartsChart);
    fixture.componentRef.setInput('build', build);
    fixture.detectChanges();
    return fixture;
  }

  const optionsOf = (fixture: ReturnType<typeof render>) => (fixture.componentInstance as unknown as { options: () => unknown }).options();

  it('builds its options from the colours of the tokens where it sits', () => {
    const fixture = render((colours) => ({ colour: colours.series.data }));
    (fixture.nativeElement as HTMLElement).style.setProperty('--chart-data', 'rgb(42, 105, 176)');
    fixture.componentRef.setInput('build', (colours: Parameters<ChartBuild>[0]) => ({ colour: colours.series.data }));
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
    theme.choose('dark');
    fixture.detectChanges();
    host.style.setProperty('--chart-data', 'rgb(94, 151, 207)');
    theme.choose('light');
    fixture.detectChanges();

    expect(optionsOf(fixture)).toEqual({ colour: 'rgb(94, 151, 207)' });
  });

  it('tells the build whether the reader asked for less motion', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => undefined, removeEventListener: () => undefined }));
    const fixture = render((_, reducedMotion) => ({ reducedMotion }));

    expect(optionsOf(fixture)).toEqual({ reducedMotion: true });
    vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: () => undefined, removeEventListener: () => undefined }));
  });
});
```

- [ ] **Step 2: Run it and watch it fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — the build cannot resolve `./echarts-chart` exports `ChartBuild` / `EchartsChart` (the renamed file still exports `EchartsBarChart`).

- [ ] **Step 3: Write the host** — `charts/echarts-chart.ts`:

```ts
import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, input } from '@angular/core';
import { ThemeStore } from '@tendero/shared-ui';
import type { EChartsCoreOption } from 'echarts/core';
import { type ChartColours, readChartColours } from './chart-colours';
import { EchartsDirective } from './echarts.directive';

/** What a chart component hands the host: its options, given the tokens' colours and the reader's motion preference. */
export type ChartBuild = (colours: ChartColours, reducedMotion: boolean) => EChartsCoreOption;

/**
 * Every ECharts chart in the backoffice, whatever it draws (R-9): the tokens'
 * colours read where the chart sits, read again when the theme changes, and the
 * reader's motion preference — once, here, rather than in each chart. Loaded in
 * the deferred chunk with ECharts itself.
 */
@Component({
  selector: 'backoffice-echarts-chart',
  imports: [EchartsDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './echarts-chart.html',
  styleUrl: './echarts-chart.css',
})
export class EchartsChart {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly theme = inject(ThemeStore);

  readonly build = input.required<ChartBuild>();

  /** Read after the theme so a switch rebuilds: the tokens change under the same element. */
  protected readonly options = computed(() => {
    this.theme.applied();
    return this.build()(readChartColours(this.host), matchMedia('(prefers-reduced-motion: reduce)').matches);
  });
}
```

`echarts-chart.html`: `<div class="plot" aria-hidden="true" [backofficeEcharts]="options()"></div>`

`echarts-chart.css`:

```css
:host { display: block; }

.plot {
  inline-size: 100%;
  block-size: var(--chart-block-size, 12.5rem);
}
```

`bar-chart.ts` — the options become a build. Import only the *type* `ChartBuild` from `./echarts-chart` (the component class enters through `imports`, which the `@defer` block keeps in its chunk):

```ts
import { EchartsChart, type ChartBuild } from './echarts-chart';
import { barOptions } from './echarts-bar-options';
// ...
  imports: [TranslocoDirective, EchartsChart],
// ...
  protected readonly build = computed<ChartBuild>(() => {
    const data = this.data();
    const format = this.format();
    return (colours, reducedMotion) => barOptions(data, format, colours, reducedMotion);
  });
```

`bar-chart.html`: in the `@defer` block, `<backoffice-echarts-chart [build]="build()" />`.

`bar-chart.css`: `.placeholder { block-size: var(--chart-block-size, 12.5rem); }`. This also closes Plan 1's deferred minor "placeholder 11rem vs plot 12.5rem".

- [ ] **Step 4: Run it and watch it pass**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: PASS, including `bar-chart.spec.ts` ("draws with ECharts, deferred") unchanged.

- [ ] **Step 5: Hand mutation** — in `echarts-chart.ts` replace `this.theme.applied();` with a comment. Run the tests. Expected: "reads the tokens again when the theme changes" FAILS. Restore in place (sed or `git checkout -- <file>`, never `mv`), then run again and expect PASS.

- [ ] **Step 6: Build**

Run: `cd frontend && npx nx build backoffice`
Expected: success; the initial total within 1 KB of Plan 1's `457.31 kB`.

- [ ] **Step 7: Commit**

```bash
git add -A frontend/apps/backoffice/src/app/charts
git commit -m "refactor(backoffice): one ECharts host for every chart, given a build of its options (R-9)

The theme-following, token-reading host was the bar chart's; the sparkline
and the row and share bars need the same, and only their options differ. The
host takes a build (colours, reduced motion -> options); the placeholder
takes the plot's height from the same custom property, so the page no longer
jumps when the chart arrives.

Hand mutation: removing the theme read from EchartsChart.options turns
\"reads the tokens again when the theme changes\" red."
```

---

### Task 2: Bars in rows and as a share

Returns by reason, refusals by reason and orders by status are rankings: bars in **rows**, with the category on the left where a long label fits. By channel and new-against-returning are parts of one whole: a single **share** bar of stacked segments. The spec's model prefers a bar to a donut for two parts, and the dataviz method keeps donuts for more. Both are layouts of the same contract (`BarChartData`), not new charts.

**Files:**
- Modify: `charts/echarts-bar-options.ts`, `charts/echarts-bar-options.spec.ts`, `charts/chart-contract.ts`, `charts/bar-chart.ts`, `charts/bar-chart.html`, `charts/bar-chart.spec.ts`

**Interfaces:**
- Consumes: `barOptions(data, format, colours, reducedMotion)` from Plan 1.
- Produces: `export type BarLayout = 'columns' | 'rows' | 'share';` in `chart-contract.ts`; `barOptions(data, format, colours, reducedMotion, layout: BarLayout = 'columns')`; `BarChart` input `layout = input<BarLayout>('columns')`. BarChart sets `--chart-block-size` from the layout: columns `12.5rem`, rows `calc(<categories> * 2rem + 1rem)`, share `3rem`.

- [ ] **Step 1: Write the failing tests** — append to `echarts-bar-options.spec.ts` (reuse its `DATA`, `COLOURS`):

```ts
describe('bars in rows and as a share', () => {
  const REASONS: BarChartData = {
    categories: ['Damaged', 'Wrong size', 'Changed mind'],
    series: [{ key: 'open', label: 'Open returns', role: 'data', values: [5, 3, 1] }],
  };
  const CHANNELS: BarChartData = {
    categories: ['Orders'],
    series: [
      { key: 'people', label: 'People', role: 'data', values: [30] },
      { key: 'agents', label: 'Agents', role: 'agent', values: [10] },
    ],
  };
  type Axis = { type: string; data?: string[]; inverse?: boolean; show?: boolean; max?: number };
  type Layout = { xAxis: Axis; yAxis: Axis; series: { stack?: string; itemStyle: { borderColor?: string; borderWidth?: number } }[] };

  it('puts the categories on the left, first at the top, when the bars are rows', () => {
    const options = barOptions(REASONS, String, COLOURS, false, 'rows') as unknown as Layout;

    expect([options.yAxis.type, options.yAxis.data, options.yAxis.inverse, options.xAxis.type]).toEqual([
      'category', ['Damaged', 'Wrong size', 'Changed mind'], true, 'value',
    ]);
  });

  it('stacks the series into one bar whose length is the whole, with no axes to read', () => {
    const options = barOptions(CHANNELS, String, COLOURS, false, 'share') as unknown as Layout;

    expect(options.series.map((series) => series.stack)).toEqual(['share', 'share']);
    expect([options.xAxis.max, options.xAxis.show, options.yAxis.show]).toEqual([40, false, false]);
  });

  /** The dataviz method's spacer: adjacent fills are parted by 2px of the surface, so two segments never read as one. */
  it('parts the segments of a share with the surface', () => {
    const options = barOptions(CHANNELS, String, COLOURS, false, 'share') as unknown as Layout;

    expect(options.series.map((series) => [series.itemStyle.borderColor, series.itemStyle.borderWidth])).toEqual([
      [COLOURS.surface, 2], [COLOURS.surface, 2],
    ]);
  });

  it('keeps drawing columns when no layout is named', () => {
    const options = barOptions(DATA, String, COLOURS, false) as unknown as Layout;

    expect([options.xAxis.type, options.yAxis.type]).toEqual(['category', 'value']);
  });
});
```

And in `bar-chart.spec.ts`:

```ts
  it('is as tall as its rows need when the bars are rows', async () => {
    const fixture = await render();
    fixture.componentRef.setInput('layout', 'rows');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).style.getPropertyValue('--chart-block-size')).toBe('calc(2 * 2rem + 1rem)');
  });
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — `'rows'` is not assignable (TS) or the assertions on `yAxis.type` fail.

- [ ] **Step 3: Implement** — `chart-contract.ts`: add

```ts
/** How the bars stand: columns over time, rows for a ranking with long labels, one stacked bar for the parts of a whole. */
export type BarLayout = 'columns' | 'rows' | 'share';
```

`echarts-bar-options.ts`:

```ts
export function barOptions(
  data: BarChartData,
  format: ValueFormat,
  colours: ChartColours,
  reducedMotion: boolean,
  layout: BarLayout = 'columns',
): EChartsCoreOption {
  const rule = { lineStyle: { color: colours.border } };
  const categoryAxis = { type: 'category', data: [...data.categories], axisTick: { show: false }, axisLabel: { color: colours.muted }, axisLine: rule };
  const valueAxis = { type: 'value', axisLabel: { formatter: (value: number) => format(value), color: colours.muted }, splitLine: rule, splitNumber: 4 };
  const whole = data.series.reduce((sum, series) => sum + (series.values[0] ?? 0), 0);

  const axes =
    layout === 'rows'
      ? { xAxis: valueAxis, yAxis: { ...categoryAxis, inverse: true } }
      : layout === 'share'
        ? { xAxis: { type: 'value', max: whole, show: false }, yAxis: { type: 'category', data: [...data.categories], show: false } }
        : { xAxis: categoryAxis, yAxis: valueAxis };

  const radius = layout === 'rows' ? [0, 4, 4, 0] : layout === 'share' ? 0 : [4, 4, 0, 0];

  return {
    animation: !reducedMotion,
    aria: { enabled: true },
    textStyle: { color: colours.text, fontFamily: colours.font },
    grid: layout === 'share' ? { left: 0, right: 0, top: 0, bottom: 0 } : { left: 8, right: 8, top: 8, bottom: 24, containLabel: true },
    tooltip: {
      trigger: layout === 'share' ? 'item' : 'axis',
      valueFormatter: (value: unknown) => format(Number(value)),
      backgroundColor: colours.surface,
      borderColor: colours.border,
      textStyle: { color: colours.text },
    },
    ...axes,
    series: data.series.map((series) => ({
      name: series.label,
      type: 'bar',
      data: [...series.values],
      barGap: '8%',
      ...(layout === 'share' ? { stack: 'share', barWidth: '100%' } : {}),
      itemStyle: {
        color: colours.series[series.role],
        borderRadius: radius,
        ...(layout === 'share' ? { borderColor: colours.surface, borderWidth: 2 } : {}),
      },
    })),
  };
}
```

`bar-chart.ts`: add `readonly layout = input<BarLayout>('columns');` and pass it through `build`. Add a host binding `host: { '[style.--chart-block-size]': 'blockSize()' }` with:

```ts
  protected readonly blockSize = computed(() => {
    const layout = this.layout();
    if (layout === 'share') return '3rem';
    if (layout === 'rows') return `calc(${this.data().categories.length} * 2rem + 1rem)`;
    return null;
  });
```

(`null` leaves the default `12.5rem`, and the wall overrides it in Task 9.)

- [ ] **Step 4: Run and watch them pass**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: PASS; the existing "carries no colour but the ones it is given" test still passes for each layout. Extend it with a loop over `['columns', 'rows', 'share']`: it must fail if a layout ever writes a colour.

- [ ] **Step 5: Commit**

```bash
git add frontend/apps/backoffice/src/app/charts
git commit -m "feat(backoffice): bars in rows for a ranking and as one share of a whole (R-9)

Returns and refusals by reason are rankings with long labels, read best as
rows; people against agents is two parts of one whole, a single stacked bar
parted by the surface (the dataviz method's spacer). Layouts of the same
contract, not new charts: every widget still hands BarChart its data."
```

---

### Task 3: The sparkline

A key figure's trend: a line with no axes, beside the number it belongs to. The number and its change are its figures (spec, *Accessibility*: "a visible number where the widget is small"), and the day-by-day values are Sales by day's table. So the line is decoration for a screen reader and carries `aria-hidden`.

**Files:**
- Create: `charts/echarts-sparkline-options.ts`, `charts/echarts-sparkline-options.spec.ts`, `charts/sparkline.{ts,html,css}`, `charts/sparkline.spec.ts`
- Modify: `charts/echarts.directive.ts` (register `LineChart`)

**Interfaces:**
- Consumes: `EchartsChart`, `ChartBuild`, `ChartColours`.
- Produces: `sparklineOptions(values: readonly number[], colours: ChartColours, reducedMotion: boolean): EChartsCoreOption`; component `Sparkline` (selector `backoffice-sparkline`, input `values: readonly number[]`).

- [ ] **Step 1: Write the failing tests** — `echarts-sparkline-options.spec.ts`:

```ts
import { describe, expect, it } from 'vitest';
import type { ChartColours } from './chart-colours';
import { sparklineOptions } from './echarts-sparkline-options';

const COLOURS: ChartColours = {
  series: { data: 'rgb(42, 105, 176)', previous: 'rgb(107, 101, 95)', agent: 'rgb(184, 69, 31)' },
  text: 'rgb(32, 29, 27)', muted: 'rgb(107, 101, 95)', border: 'rgb(228, 224, 217)', surface: 'rgb(255, 255, 255)', font: 'Instrument Sans',
};

type Spark = {
  xAxis: { show: boolean; boundaryGap: boolean }; yAxis: { show: boolean; scale: boolean };
  series: { type: string; data: number[]; symbol: string; lineStyle: { color: string; width: number } }[];
  animation: boolean; tooltip?: unknown;
};

describe('a sparkline', () => {
  it('draws one 2px line in the data colour, with no axes, no markers and no tooltip', () => {
    const options = sparklineOptions([3, 5, 4], COLOURS, false) as unknown as Spark;

    expect(options.series).toEqual([expect.objectContaining({ type: 'line', data: [3, 5, 4], symbol: 'none', lineStyle: { color: COLOURS.series.data, width: 2 } })]);
    expect([options.xAxis.show, options.yAxis.show, options.tooltip]).toEqual([false, false, undefined]);
  });

  /** A trend is its shape: starting the scale at zero would flatten a week that moved between 90 and 110. */
  it('scales to the values rather than from zero, and runs edge to edge', () => {
    const options = sparklineOptions([90, 110], COLOURS, false) as unknown as Spark;

    expect([options.yAxis.scale, options.xAxis.boundaryGap]).toEqual([true, false]);
  });

  it('stays still when the reader asked for less motion', () => {
    expect((sparklineOptions([1], COLOURS, true) as unknown as Spark).animation).toBe(false);
  });
});
```

`sparkline.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Sparkline } from './sparkline';

describe('the sparkline component', () => {
  it('is hidden from a screen reader, whose figures are the number beside it', () => {
    const fixture = TestBed.createComponent(Sparkline);
    fixture.componentRef.setInput('values', [1, 2, 3]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).getAttribute('aria-hidden')).toBe('true');
  });

  it('draws with ECharts, deferred until it is on screen', async () => {
    const fixture = TestBed.createComponent(Sparkline);
    fixture.componentRef.setInput('values', [1, 2, 3]);
    fixture.detectChanges();

    expect(await fixture.getDeferBlocks()).toHaveLength(1);
  });
});
```

- [ ] **Step 2: Run and watch them fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — the modules `./echarts-sparkline-options` and `./sparkline` do not exist.

- [ ] **Step 3: Implement**

`echarts-sparkline-options.ts`:

```ts
import type { EChartsCoreOption } from 'echarts/core';
import type { ChartColours } from './chart-colours';

/** A key figure's trend (R-9): its shape only — the number and its change sit beside it. */
export function sparklineOptions(values: readonly number[], colours: ChartColours, reducedMotion: boolean): EChartsCoreOption {
  return {
    animation: !reducedMotion,
    grid: { left: 1, right: 1, top: 2, bottom: 2 },
    xAxis: { type: 'category', show: false, boundaryGap: false, data: values.map((_, index) => index) },
    yAxis: { type: 'value', show: false, scale: true },
    series: [{ type: 'line', data: [...values], symbol: 'none', lineStyle: { color: colours.series.data, width: 2 } }],
  };
}
```

`sparkline.ts`:

```ts
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { EchartsChart, type ChartBuild } from './echarts-chart';
import { sparklineOptions } from './echarts-sparkline-options';

/**
 * A trend beside a key figure (R-9). Hidden from a screen reader on purpose: the
 * figure and its change beside it are what it says, and the day-by-day values are
 * Sales by day's table.
 */
@Component({
  selector: 'backoffice-sparkline',
  imports: [EchartsChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sparkline.html',
  styleUrl: './sparkline.css',
  host: { 'aria-hidden': 'true' },
})
export class Sparkline {
  readonly values = input.required<readonly number[]>();

  protected readonly build = computed<ChartBuild>(() => {
    const values = this.values();
    return (colours, reducedMotion) => sparklineOptions(values, colours, reducedMotion);
  });
}
```

`sparkline.html`:

```html
@defer (on viewport) {
  <backoffice-echarts-chart [build]="build()" />
} @placeholder {
  <div class="placeholder"></div>
}
```

`sparkline.css`:

```css
:host { display: block; --chart-block-size: 2.5rem; }
.placeholder { block-size: var(--chart-block-size); }
```

`echarts.directive.ts`: `import { BarChart, LineChart } from 'echarts/charts';` and `echarts.use([BarChart, LineChart, GridComponent, TooltipComponent, AriaComponent, SVGRenderer]);`

- [ ] **Step 4: Run and watch them pass; build**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache && npx nx build backoffice`
Expected: PASS. The initial total must not grow: `LineChart` joins the deferred chunk. Record that chunk's size before and after in the commit message.

- [ ] **Step 5: Commit**

```bash
git add frontend/apps/backoffice/src/app/charts
git commit -m "feat(backoffice): a sparkline for a key figure's trend (R-9)

A line with no axes, scaled to its values so a week that moved between 90 and
110 does not read flat, hidden from a screen reader whose figures are the
number and change beside it. LineChart joins the deferred chunk: <before> ->
<after> KB."
```

---

### Task 4: The size contract and the grid

A dashboard becomes data: a list of placements, each a widget with its size. The grid lays out 12 columns (S 3, M 6, L 12). Each cell is a container, so a widget chooses what to show from **its own width** (spec: "never the screen's"). The grid itself follows the screen: two S side by side on a laptop, one column on a phone.

**Files:**
- Create: `features/dashboard/dashboard-widgets.ts`, `features/dashboard/dashboard-grid.{ts,html,css}`, `features/dashboard/dashboard-grid.spec.ts`
- Modify: `apps/backoffice/src/styles.css` (size-contract classes), `charts/bar-chart.css` (table visible at L)

**Interfaces:**
- Consumes: `DashboardPeriod` (`data-access/ordering.service`).
- Produces (`dashboard-widgets.ts`):

```ts
import { InjectionToken, type Type } from '@angular/core';

/** The size contract (spec): S shows the figure, M the chart, L the chart and its table — S, M and L are 3, 6 and 12 of 12 columns. */
export type WidgetSize = 'S' | 'M' | 'L';

export type WidgetKey =
  | 'keyFigures' | 'salesByDay' | 'bestSellers' | 'byChannel'
  | 'waiting' | 'health' | 'ordersByStatus' | 'funnel' | 'returns' | 'stock'
  | 'customers' | 'agentSales' | 'refusals';

/** One widget on one dashboard, at one size: a dashboard is a list of these, which is what v2 will let a person edit. */
export interface WidgetPlacement {
  readonly widget: WidgetKey;
  readonly size: WidgetSize;
}

/** What the grid needs to draw a widget: its component, and whether it takes the dashboard's period. */
export interface WidgetDefinition {
  readonly component: Type<unknown>;
  readonly takesPeriod: boolean;
}

export const DASHBOARD_WIDGETS = new InjectionToken<Readonly<Record<WidgetKey, WidgetDefinition>>>('DASHBOARD_WIDGETS');
```

- Component `DashboardGrid` (selector `backoffice-dashboard-grid`, inputs `placements: readonly WidgetPlacement[]`, `period: DashboardPeriod`, `fill = input(false)` for Task 9's wall). Each cell is `<div class="cell cell--S|M|L" data-widget="<key>">`.
- Global classes (styles.css): `.at-m` shown from M, `.at-l` shown from L, `.only-s` shown only at S.

**Ruling to ledger when this task starts:** the spec's "a chart's marks are reachable by keyboard where they carry a tooltip" is **not** built in Plan 2. The drawing stays `aria-hidden`, and every value its tooltip shows is in the chart's table, which the keyboard reaches: through the toggle at M, directly at L, and at S the figure is the text itself. Focusable marks mean ECharts' keyboard handling or our own overlay of buttons, which is a decision of its own. It goes to Plan 4 with the other accessibility checks. Cost if wrong: a keyboard user reads the numbers in a table rather than on the bars for one more plan.

- [ ] **Step 1: Write the failing test** — `dashboard-grid.spec.ts`:

```ts
import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DashboardGrid } from './dashboard-grid';
import { DASHBOARD_WIDGETS, type WidgetDefinition, type WidgetKey } from './dashboard-widgets';

@Component({ selector: 'stub-with-period', template: '<p>period {{ period() }}</p>' })
class WithPeriod {
  readonly period = input.required<string>();
}

@Component({ selector: 'stub-without', template: '<p>no period</p>' })
class WithoutPeriod {}

function render(placements: { widget: WidgetKey; size: 'S' | 'M' | 'L' }[], period = 'Week') {
  const widgets = { salesByDay: { component: WithPeriod, takesPeriod: true }, waiting: { component: WithoutPeriod, takesPeriod: false } } as unknown as Record<WidgetKey, WidgetDefinition>;
  TestBed.configureTestingModule({ providers: [{ provide: DASHBOARD_WIDGETS, useValue: widgets }] });
  const fixture = TestBed.createComponent(DashboardGrid);
  fixture.componentRef.setInput('placements', placements);
  fixture.componentRef.setInput('period', period);
  fixture.detectChanges();
  return fixture;
}

const cells = (element: HTMLElement) => [...element.querySelectorAll<HTMLElement>('.cell')];

describe('the dashboard grid', () => {
  it('lays the widgets out in their order, each at its size', () => {
    const element = render([{ widget: 'salesByDay', size: 'M' }, { widget: 'waiting', size: 'S' }]).nativeElement as HTMLElement;

    expect(cells(element).map((cell) => [cell.dataset['widget'], [...cell.classList].find((name) => name.startsWith('cell--'))])).toEqual([
      ['salesByDay', 'cell--M'], ['waiting', 'cell--S'],
    ]);
  });

  it('gives the period to the widgets that take one, and follows it when it changes', () => {
    const fixture = render([{ widget: 'salesByDay', size: 'M' }, { widget: 'waiting', size: 'S' }]);
    fixture.componentRef.setInput('period', 'Month');
    fixture.detectChanges();

    expect(cells(fixture.nativeElement).map((cell) => cell.textContent?.trim())).toEqual(['period Month', 'no period']);
  });

  it('marks itself to fill the screen when it is on a wall', () => {
    const fixture = render([{ widget: 'waiting', size: 'S' }]);
    fixture.componentRef.setInput('fill', true);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.grid')?.classList.contains('grid--fill')).toBe(true);
  });
});
```

- [ ] **Step 2: Run and watch it fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — `./dashboard-grid` and `./dashboard-widgets` do not exist.

- [ ] **Step 3: Implement** — `dashboard-grid.ts`:

```ts
import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DASHBOARD_WIDGETS, type WidgetPlacement } from './dashboard-widgets';

/**
 * A dashboard drawn from its placements (R-9): which widget, at which size, in
 * which order. The layout is data and the widgets come from a registry, so a
 * tab is a list, and v2's configurable dashboard edits a list rather than a
 * template.
 */
@Component({
  selector: 'backoffice-dashboard-grid',
  imports: [NgComponentOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-grid.html',
  styleUrl: './dashboard-grid.css',
})
export class DashboardGrid {
  private readonly widgets = inject(DASHBOARD_WIDGETS);

  readonly placements = input.required<readonly WidgetPlacement[]>();
  readonly period = input.required<DashboardPeriod>();
  /** On a wall (Task 9): the rows share the screen's height. */
  readonly fill = input(false);

  protected readonly cells = computed(() =>
    this.placements().map((placement) => {
      const definition = this.widgets[placement.widget];
      return {
        key: placement.widget,
        size: placement.size,
        component: definition.component,
        inputs: definition.takesPeriod ? { period: this.period() } : {},
      };
    }),
  );
}
```

`dashboard-grid.html`:

```html
<div class="grid" [class.grid--fill]="fill()">
  @for (cell of cells(); track cell.key) {
    <div [class]="'cell cell--' + cell.size" [attr.data-widget]="cell.key">
      <ng-container *ngComponentOutlet="cell.component; inputs: cell.inputs" />
    </div>
  }
</div>
```

`dashboard-grid.css`:

```css
/* Twelve columns: S, M and L take 3, 6 and 12 (spec, the size contract). The
   grid follows the screen; what each widget shows follows its own width. */
.grid {
  display: grid;
  grid-template-columns: repeat(12, minmax(0, 1fr));
  gap: var(--space-4);
  padding-block-end: var(--space-6);
}

/* A cell is the container its widget measures itself against. min-width: 0,
   because a grid item is as wide as its content by default and a best seller's
   name is long (found at 390 px in P11-7). */
.cell {
  container: widget / inline-size;
  min-width: 0;
}

.cell > * { display: block; block-size: 100%; }

.cell--S { grid-column: span 3; }
.cell--M { grid-column: span 6; }
.cell--L { grid-column: span 12; }

/* A laptop: two S side by side, an M across. */
@media (max-width: 75rem) {
  .cell--S { grid-column: span 6; }
  .cell--M { grid-column: span 12; }
}

/* A phone: one widget under another, each at the phone's width (spec, the phone). */
@media (max-width: 40rem) {
  .cell--S, .cell--M, .cell--L { grid-column: span 12; }
}
```

`styles.css`, after the dashboard widget block:

```css
/* ---------- The size contract (R-9) ----------
   A widget shows its figure at S, its chart from M, its chart and table at L —
   decided by its own width, measured on the grid cell it sits in (container
   `widget`), never by the screen's. In rem, so a wall's larger type (R-9's full
   screen) moves the thresholds with it. */
@container widget (width < 20rem) {
  .at-m, .at-l { display: none; }
}

@container widget (width < 56rem) {
  .at-l { display: none; }
}

@container widget (width >= 20rem) {
  .only-s { display: none; }
}
```

`bar-chart.css` — the table is shown without asking at L (spec, *Accessibility*). This reverses Plan 1's ruling ("a toggle at every size"): now that widgets have a container, the spec's own rule applies. Ledger it as a Ruling.

```css
@container widget (width >= 56rem) {
  .figures.sr-only {
    position: static;
    inline-size: auto;
    block-size: auto;
    margin: 0;
    overflow: visible;
    clip-path: none;
    white-space: normal;
  }

  .figures-toggle { display: none; }
}
```

- [ ] **Step 4: Run and watch it pass**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: PASS. jsdom cannot evaluate container queries, so the CSS half of this task is held by Task 10's Playwright spec. Say so in the commit.

- [ ] **Step 5: Commit**

```bash
git add frontend/apps/backoffice/src
git commit -m "feat(backoffice): a dashboard is a list of placements on a twelve-column grid (R-9)

Each placement is a widget and a size — S, M or L, 3, 6 or 12 columns — and
each cell a container, so a widget chooses its figure, its chart or its
table from its own width, never the screen's. The chart's table now shows
without asking at L, as the spec's accessibility section says. jsdom cannot
evaluate container queries; the browser spec (Task 10) holds that half."
```

---

### Task 5: Shared reads, and the Sales widgets

The Overview puts key figures, best sellers and by channel on one screen. The first two read `/sales` and the last `/agent-sales`, and key figures also reads `/funnel`, `/operations` and the series. Six widgets must not mean ten requests. `DashboardSources` shares each read by endpoint, period and refresh instant. `DashboardRefresh` is that instant: here it only records the page's load, and Task 9 makes it tick. Then the Sales widgets are built from the old `SalesWidget`, which is deleted.

**Files:**
- Create: `features/dashboard/dashboard-refresh.ts`, `features/dashboard/dashboard-sources.ts`, `features/dashboard/dashboard-sources.spec.ts`, `features/dashboard/key-figures.ts`, `features/dashboard/key-figures.spec.ts`, `features/dashboard/key-figures-widget.{ts,html,css}`, `features/dashboard/best-sellers-widget.{ts,html,css}`, `features/dashboard/by-channel-widget.{ts,html,css}`, `features/dashboard/by-channel-widget.spec.ts`
- Modify: `features/dashboard/sales-by-day-widget.ts` (+ its spec's providers), `features/dashboard/dashboard-page.{ts,html}`, i18n en/es
- Delete: `features/dashboard/sales-widget.{ts,html,css}`

**Interfaces:**
- Consumes: `OrderingService.sales|salesSeries|funnel|operations|agentSales(period)`, `AccountsService.agentDenials(period)`, `InventoryService.stock(culture, filter)`, `changeOf`, `formatChange`, `formatShare`, `shareOf` (`figures.ts`), `Sparkline`, `BarChart` (layout `share`).
- Produces:

```ts
// dashboard-refresh.ts
@Injectable()
export class DashboardRefresh {
  /** When the figures on screen were asked for, in epoch ms. Part of every widget's request, so a new instant asks again. */
  readonly at: Signal<number>;
  refresh(): void;
}

// dashboard-sources.ts
@Injectable()
export class DashboardSources {
  sales(period: DashboardPeriod, at: number): Observable<SalesSummary>;
  series(period: DashboardPeriod, at: number): Observable<SalesSeries>;
  funnel(period: DashboardPeriod, at: number): Observable<CheckoutFunnel>;
  operations(period: DashboardPeriod, at: number): Observable<OperationsSummary>;
  agentSales(period: DashboardPeriod, at: number): Observable<AgentSales>;
  agentDenials(period: DashboardPeriod, at: number): Observable<AgentDenialsSummary>;
  stock(culture: string, at: number): Observable<StockList>;   // filter 'Restock', the tasks' and stock widgets' read
}

// key-figures.ts
export type KeyFigureKey = 'revenue' | 'orders' | 'units' | 'averageOrder' | 'conversion' | 'returnRate';
export interface KeyFigure { readonly key: KeyFigureKey; readonly value: string; readonly change: Change | null; readonly trend: readonly number[] | null; }
export interface KeyFigureSources { readonly sales?: SalesSummary; readonly series?: SalesSeries; readonly funnel?: CheckoutFunnel; readonly operations?: OperationsSummary; }
export interface KeyFigureFormats { readonly money: (amount: number) => string; readonly share: (ratio: number) => string; }
export function keyFigures(sources: KeyFigureSources, say: KeyFigureFormats): KeyFigure[];
```

Widgets: `KeyFiguresWidget` (`backoffice-key-figures-widget`, input `period`), `BestSellersWidget` (`backoffice-best-sellers-widget`, input `period`), `ByChannelWidget` (`backoffice-by-channel-widget`, input `period`). Every widget injects `DashboardSources` and `DashboardRefresh`, and its `rxResource` params are `{ period: this.period(), at: this.refresh.at() }`.

**Ruling to ledger when this task starts:** conversion and return rate have no previous period and no series today (`CheckoutFunnel`, `OperationsSummary`). They show their figure with no change and no sparkline until Plan 3's D-7 adds both. The spec says "each with a sparkline and its change", and its data table names the series only for revenue, orders and units. Cost if wrong: two figures stay bare until Plan 3.

- [ ] **Step 1: Write the failing tests** — `dashboard-sources.spec.ts`:

```ts
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { DashboardSources } from './dashboard-sources';

const Sales = 'http://api/api/dashboard/sales';

describe('the dashboard sources', () => {
  let http: HttpTestingController;
  let sources: DashboardSources;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), DashboardSources, { provide: API_BASE_URL, useValue: 'http://api' }],
    });
    http = TestBed.inject(HttpTestingController);
    sources = TestBed.inject(DashboardSources);
  });

  it('asks an endpoint once for every widget that reads it in the same period and instant', async () => {
    const first = firstValueFrom(sources.sales('Week', 1));
    const second = firstValueFrom(sources.sales('Week', 1));
    http.expectOne((req) => req.url === Sales).flush({ period: 'Week' });

    expect([await first, await second]).toEqual([{ period: 'Week' }, { period: 'Week' }]);
  });

  it('asks again for another period, and for a later instant', () => {
    sources.sales('Week', 1).subscribe();
    sources.sales('Month', 1).subscribe();
    sources.sales('Week', 2).subscribe();

    expect(http.match((req) => req.url === Sales)).toHaveLength(3);
  });

  /** A failure is not an answer to keep: the next widget or the next refresh asks again. */
  it('forgets a read that failed', async () => {
    const failed = firstValueFrom(sources.sales('Week', 1)).catch(() => 'failed');
    http.expectOne((req) => req.url === Sales).flush('down', { status: 503, statusText: 'Unavailable' });
    expect(await failed).toBe('failed');

    sources.sales('Week', 1).subscribe();
    http.expectOne((req) => req.url === Sales);
  });
});
```

`key-figures.spec.ts`:

```ts
import type { CheckoutFunnel, OperationsSummary, SalesSeries, SalesSummary } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { keyFigures } from './key-figures';

const say = { money: (amount: number) => `€${amount}`, share: (ratio: number) => `${Math.round(ratio * 100)}%` };
const figures = (revenue: number, orders: number, units: number, averageOrderValue: number) => ({ revenue, orders, units, averageOrderValue });
const SALES = { period: 'Week', currency: 'EUR', current: figures(900, 9, 12, 100), previous: figures(600, 6, 9, 100), bestSellers: [] } as unknown as SalesSummary;
const point = (revenue: number, orders: number, units: number) => ({ start: '2026-10-12T00:00:00+02:00', revenue, orders, units });
const SERIES = { current: [point(300, 3, 4), point(0, 0, 0), point(600, 6, 8)], previous: [] } as unknown as SalesSeries;

describe('the key figures', () => {
  it('says revenue, orders, units and the average order with their change and their trend', () => {
    const result = keyFigures({ sales: SALES, series: SERIES }, say);

    expect(result.map((figure) => [figure.key, figure.value, figure.change?.direction, figure.trend])).toEqual([
      ['revenue', '€900', 'up', [300, 0, 600]],
      ['orders', '9', 'up', [3, 0, 6]],
      ['units', '12', 'up', [4, 0, 8]],
      // A day with no orders has no average: 0 on the line, never NaN.
      ['averageOrder', '€100', 'flat', [100, 0, 100]],
    ]);
  });

  it('draws no trend until the series has arrived', () => {
    expect(keyFigures({ sales: SALES }, say).every((figure) => figure.trend === null)).toBe(true);
  });

  /** Plan 2's ruling: no previous period for these two today, so no change and no trend rather than an invented one. */
  it('says conversion and the return rate bare, once their sources arrive', () => {
    const funnel = { conversion: 0.25 } as CheckoutFunnel;
    const operations = { returnRate: 0.05 } as OperationsSummary;

    expect(keyFigures({ funnel, operations }, say)).toEqual([
      { key: 'conversion', value: '25%', change: null, trend: null },
      { key: 'returnRate', value: '5%', change: null, trend: null },
    ]);
  });
});
```

`by-channel-widget.spec.ts` (set up like `sales-by-day-widget.spec.ts`: `provideTenderoI18n`, `HttpTestingController`, `API_BASE_URL`, and the providers `DashboardSources`, `DashboardRefresh`):

```ts
  const AGENTS = (orders: number, agentOrders: number) => ({
    period: 'Week', currency: 'EUR', orders, revenue: orders * 50, agentOrders, agentRevenue: agentOrders * 50,
    mandated: 0, rehearsals: 0, byAgent: [],
  });

  it('shares the orders between people and agents, agents in clay', async () => {
    const element = await open(AGENTS(40, 10));

    expect([...element.querySelectorAll('.legend li')].map((item) => item.textContent?.trim())).toEqual(['People', 'Agents']);
    expect([...element.querySelectorAll('table td')].map((cell) => cell.textContent?.trim())).toEqual(['30', '10']);
  });

  /** Review focus 5: nothing sold is a sentence, not an empty bar. */
  it('says nothing was sold rather than drawing a share of nothing', async () => {
    const element = await open(AGENTS(0, 0));

    expect(element.querySelector('backoffice-bar-chart')).toBeNull();
    expect(element.textContent).toContain('Nothing sold in this period.');
  });
```

where `open(body)` creates the widget with `period` `'Week'`, flushes the en translations with the real `dashboard.byChannel` words (read the JSON as `sales-by-day-widget.spec.ts` does), flushes `http://api/api/dashboard/agent-sales` with `body`, awaits `whenStable()` and returns the element.

- [ ] **Step 2: Run them and watch them fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — missing modules `./dashboard-sources`, `./key-figures`, `./by-channel-widget`.

- [ ] **Step 3: Implement the clock and the sources**

`dashboard-refresh.ts`:

```ts
import { Injectable, signal } from '@angular/core';

/**
 * When the dashboard's figures were asked for (R-9). Every widget puts this
 * instant in its request, so moving it asks every widget again at once — by
 * hand now, every minute on a wall (Task 9), and from a live source in R-10.
 */
@Injectable()
export class DashboardRefresh {
  private readonly instant = signal(Date.now());
  readonly at = this.instant.asReadonly();

  refresh(): void {
    this.instant.set(Date.now());
  }
}
```

`dashboard-sources.ts`:

```ts
import { inject, Injectable } from '@angular/core';
import type { AgentDenialsSummary, AgentSales, CheckoutFunnel, OperationsSummary, SalesSeries, SalesSummary } from '@tendero/shared-api';
import { catchError, type Observable, shareReplay, throwError } from 'rxjs';
import { AccountsService } from '../../data-access/accounts.service';
import { InventoryService, type StockList } from '../../data-access/inventory.service';
import { type DashboardPeriod, OrderingService } from '../../data-access/ordering.service';

/**
 * The dashboard's reads, shared (R-9). Widgets are independent — each asks for
 * what it shows — but six on one screen must not mean ten requests: a read is
 * kept per endpoint, period and instant, so the widgets on the same figures
 * share one answer, and a new instant (a refresh) asks again. Provided by the
 * dashboard page, so leaving it forgets everything.
 */
@Injectable()
export class DashboardSources {
  private readonly ordering = inject(OrderingService);
  private readonly accounts = inject(AccountsService);
  private readonly inventory = inject(InventoryService);
  private readonly kept = new Map<string, Observable<unknown>>();
  private keptAt = Number.NaN;

  sales = (period: DashboardPeriod, at: number) => this.shared<SalesSummary>(`sales:${period}`, at, () => this.ordering.sales(period));
  series = (period: DashboardPeriod, at: number) => this.shared<SalesSeries>(`series:${period}`, at, () => this.ordering.salesSeries(period));
  funnel = (period: DashboardPeriod, at: number) => this.shared<CheckoutFunnel>(`funnel:${period}`, at, () => this.ordering.funnel(period));
  operations = (period: DashboardPeriod, at: number) => this.shared<OperationsSummary>(`operations:${period}`, at, () => this.ordering.operations(period));
  agentSales = (period: DashboardPeriod, at: number) => this.shared<AgentSales>(`agentSales:${period}`, at, () => this.ordering.agentSales(period));
  agentDenials = (period: DashboardPeriod, at: number) =>
    this.shared<AgentDenialsSummary>(`agentDenials:${period}`, at, () => this.accounts.agentDenials(period));
  stock = (culture: string, at: number) => this.shared<StockList>(`stock:${culture}`, at, () => this.inventory.stock(culture, 'Restock'));

  private shared<T>(key: string, at: number, read: () => Observable<T>): Observable<T> {
    // A new instant makes every kept answer old: forget them all at once.
    if (at !== this.keptAt) {
      this.kept.clear();
      this.keptAt = at;
    }

    let answer = this.kept.get(key) as Observable<T> | undefined;
    if (!answer) {
      answer = read().pipe(
        catchError((failure: unknown) => {
          this.kept.delete(key);
          return throwError(() => failure);
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
      this.kept.set(key, answer);
    }

    return answer;
  }
}
```

Check the exact exported names of `AgentDenialsSummary` in `@tendero/shared-api` and of `StockList` in `inventory.service.ts` with `node tools/api-map.mjs --schema AgentDenialsSummary` and a grep. If a name differs, use the real one and ledger it.

- [ ] **Step 4: Implement the figures and the widgets**

`key-figures.ts`:

```ts
import type { CheckoutFunnel, OperationsSummary, SalesSeries, SalesSummary } from '@tendero/shared-api';
import { type Change, changeOf } from './figures';

// (the Produces types above)

/**
 * The Overview's first row (spec, the Overview): the figures a shopkeeper asks
 * first, each with how it moved and its trend where both exist. Only saying
 * them: every number was computed by the server. Each source is optional
 * because each arrives on its own, and a figure appears when its source does.
 */
export function keyFigures(sources: KeyFigureSources, say: KeyFigureFormats): KeyFigure[] {
  const { sales, series, funnel, operations } = sources;
  const trend = (pick: (point: SalesSeries['current'][number]) => number) => series?.current.map(pick) ?? null;
  const figures: KeyFigure[] = [];

  if (sales) {
    const { current, previous } = sales;
    figures.push(
      { key: 'revenue', value: say.money(current.revenue), change: changeOf(current.revenue, previous.revenue), trend: trend((point) => point.revenue) },
      { key: 'orders', value: String(current.orders), change: changeOf(current.orders, previous.orders), trend: trend((point) => point.orders) },
      { key: 'units', value: String(current.units), change: changeOf(current.units, previous.units), trend: trend((point) => point.units) },
      {
        key: 'averageOrder',
        value: say.money(current.averageOrderValue),
        change: changeOf(current.averageOrderValue, previous.averageOrderValue),
        trend: trend((point) => (point.orders === 0 ? 0 : point.revenue / point.orders)),
      },
    );
  }

  if (funnel) figures.push({ key: 'conversion', value: say.share(funnel.conversion), change: null, trend: null });
  if (operations) figures.push({ key: 'returnRate', value: say.share(operations.returnRate), change: null, trend: null });
  return figures;
}
```

`key-figures-widget.ts`: four `rxResource`s (sales, series, funnel, operations) on `DashboardSources` with params `{ period: this.period(), at: this.refresh.at() }`. `figures = computed(() => keyFigures({ sales: this.sales.value(), series: this.series.value(), funnel: this.funnel.value(), operations: this.operations.value() }, { money, share }))`, where `money` uses `formatPrice(amount, sales.currency ?? 'EUR', culture)` and `share` uses `formatShare(ratio, culture)`. It shows loading until `sales` has a value and the failed state if `sales.error()`. The other three are optional: while missing they simply add no figure.

`key-figures-widget.html` (prefix `dashboard.keyFigures`):

```html
<article class="widget panel" *transloco="let t; prefix: 'dashboard.keyFigures'">
  <header class="widget__head">
    <h2 class="widget__title">{{ t('title') }}</h2>
    <span class="eyebrow">{{ t('vsPrevious') }}</span>
  </header>
  @if (sales.isLoading() && !sales.hasValue()) {
    <p class="muted widget__state" role="status">{{ t('loading') }}</p>
  } @else if (sales.error()) {
    <p class="failed widget__state" role="alert">{{ t('failed') }}</p>
  } @else {
    <dl class="kpis">
      @for (figure of figures(); track figure.key) {
        <div class="kpi">
          <dt class="kpi__label">{{ t(figure.key) }}</dt>
          <dd class="kpi__value numeric">{{ figure.value }}</dd>
          @if (figure.change; as change) {
            <dd [class]="'kpi__change kpi__change--' + change.direction">
              <span aria-hidden="true">{{ change.direction === 'up' ? '▲' : change.direction === 'down' ? '▼' : '—' }}</span>
              {{ changeText(change) ?? t('direction.' + change.direction) }}
            </dd>
          }
          @if (figure.trend; as trend) {
            <dd class="kpi__trend at-m"><backoffice-sparkline [values]="trend" /></dd>
          }
        </div>
      }
    </dl>
  }
</article>
```

`key-figures-widget.css`: move the `.kpis`, `.kpi*` rules from `sales-widget.css`. `.kpis` becomes `display: grid; grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr)); gap: var(--space-4);`, which is six across at L and two at a phone's ≈ 22rem (spec, *The phone*: "the key figures go to two columns").

`best-sellers-widget.{ts,html,css}`: the ranking half of `sales-widget.*`, moved as-is, reading `sources.sales`. Its title is `dashboard.bestSellers.title`.

`by-channel-widget.ts`: reads `sources.agentSales`. Its labels come from `translateObjectSignal('dashboard.byChannel')` (reactive to language, the Plan 1 finding). It builds:

```ts
  protected readonly chart = computed<BarChartData | null>(() => {
    const sales = this.sales.value();
    const words = this.words() as Partial<Record<'people' | 'agents' | 'orders', string>>;
    if (!sales || sales.orders === 0) return null;
    return {
      categories: [words.orders ?? ''],
      series: [
        { key: 'people', label: words.people ?? '', role: 'data', values: [sales.orders - sales.agentOrders] },
        { key: 'agents', label: words.agents ?? '', role: 'agent', values: [sales.agentOrders] },
      ],
    };
  });
```

`by-channel-widget.html` (prefix `dashboard.byChannel`): the loading and failed states as in the others. With `sales.orders === 0` it shows `<p class="muted widget__state">{{ t('nothingSold') }}</p>`. Otherwise:

```html
    <p class="only-s share-figure"><span class="numeric">{{ agentShare() }}</span> {{ t('byAgents') }}</p>
    <div class="at-m"><backoffice-bar-chart [data]="data" layout="share" [format]="count" [caption]="t('title')" /></div>
    <p class="muted widget__note">{{ t('revenueShare', { share: revenueShare() }) }}</p>
```

`agentShare` and `revenueShare` use `formatShare(shareOf(...), culture)`. `count = (value: number) => String(value)`.

`sales-by-day-widget.ts`: its resource streams `this.sources.series(params.period, params.at)` with `at` in the params. Its spec's providers gain `DashboardSources, DashboardRefresh`.

`dashboard-page.ts`: `providers: [DashboardSources, DashboardRefresh]`. Replace `SalesWidget` with `KeyFiguresWidget` + `BestSellersWidget` + `ByChannelWidget` in `imports` and the template. Delete `sales-widget.*`.

i18n — insert beside `dashboard.sales` in en and es (then delete `dashboard.sales` once nothing uses it; grep `dashboard.sales\b` and `'sales'` prefixes first):

| key | en | es |
|---|---|---|
| `dashboard.keyFigures.title` | Key figures | Cifras clave |
| `…vsPrevious` | Against the previous period | Frente al periodo anterior |
| `…loading` / `…failed` | Loading key figures… / The key figures could not be loaded. | Cargando las cifras clave… / No se han podido cargar las cifras clave. |
| `…revenue` `…orders` `…units` `…averageOrder` | (the current `dashboard.sales` words) | (the current ones) |
| `…conversion` / `…returnRate` | Conversion / Return rate | Conversión / Tasa de devolución |
| `…direction.{up,down,flat}` | (the current ones) | (the current ones) |
| `dashboard.bestSellers.{title,loading,failed,nothingSold,unitCount}` | Best sellers / Loading… / The best sellers could not be loaded. / Nothing sold in this period. / {{count}} u. | Más vendidos / Cargando… / No se han podido cargar los más vendidos. / No se ha vendido nada en este periodo. / {{count}} u. |
| `dashboard.byChannel.{title,loading,failed,nothingSold,orders,people,agents,byAgents,revenueShare}` | By channel / Loading… / The channels could not be loaded. / Nothing sold in this period. / Orders / People / Agents / of orders by agents / {{share}} of sales came through agents | Por canal / Cargando… / No se han podido cargar los canales. / No se ha vendido nada en este periodo. / Pedidos / Personas / Agentes / de los pedidos, por agentes / {{share}} de las ventas llegó por agentes |

Read the current es words for the reused keys from `es.json` and copy them. Do not translate them afresh.

- [ ] **Step 5: Run and watch them pass**

Run: `cd frontend && npx nx run-many -t lint test -p backoffice shared-i18n --skip-nx-cache`
Expected: PASS (shared-i18n holds es/en parity and the token rule).

- [ ] **Step 6: Hand mutation** — in `dashboard-sources.ts` remove the `this.kept.delete(key);` line. Run. Expected: "forgets a read that failed" FAILS. Restore in place and run again: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A frontend/apps/backoffice
git commit -m "feat(backoffice): key figures with their trends, best sellers and the channels, on shared reads (R-9)

Six widgets on one screen must not mean ten requests: DashboardSources keeps
each read per endpoint, period and instant, so widgets on the same figures
share one answer and a failure is forgotten for the next to ask again. The
old sales widget splits in two — the key figures, now with conversion and
the return rate and a sparkline where a series exists, and the best sellers —
and people against agents is one share bar, agents in clay.

Hand mutation: keeping a failed read in DashboardSources turns \"forgets a
read that failed\" red."
```

---

### Task 6: The Operations widgets

`OperationsWidget` carried orders, health and returns in one panel. The spec's Operations tab names them apart, with the stock and the checkout funnel: *Orders by status · Health · Checkout funnel · Returns (by reason) · Stock · Waiting for you*.

**Files:**
- Create: `features/dashboard/orders-by-status-widget.{ts,html,css}`, `health-widget.{ts,html,css}`, `returns-widget.{ts,html,css}`, `stock-widget.{ts,html,css}`, `returns-widget.spec.ts`, `stock-widget.spec.ts`
- Modify: `funnel-widget.ts` (reads through the sources), `tasks-widget.ts` (stock through the sources; `at` in every param), `dashboard-page.{ts,html}`, i18n
- Delete: `operations-widget.{ts,html,css}`

**Interfaces:**
- Consumes: `DashboardSources.operations|funnel|stock`, `DashboardRefresh.at`, `BarChart` (layout `rows`), `bySkuLowestFirst`, `formatShare`, `formatPrice`.
- Produces: `OrdersByStatusWidget` (`backoffice-orders-by-status-widget`, input `period`), `HealthWidget` (`backoffice-health-widget`, input `period`), `ReturnsWidget` (`backoffice-returns-widget`, input `period`), `StockWidget` (`backoffice-stock-widget`, no period: stock is now, not a window).

- [ ] **Step 1: Write the failing tests** (set up as `by-channel-widget.spec.ts`)

`returns-widget.spec.ts`:

```ts
  const OPS = {
    period: 'Week', currency: 'EUR', toShip: 0, lateToShip: 0, inProgress: 0, paymentFailed: 0, cancelled: 0,
    cancellationRate: 0, cancellationTarget: 0.02, cancellationStanding: 'within', openReturns: 4,
    openReturnsByReason: [{ reason: 'Damaged', count: 3 }, { reason: 'WrongSize', count: 1 }], refunded: 80, returnRate: 0.05,
  };

  /** The reasons' words are the returns screen's own (P11-7): one translation of five words, not two that drift. */
  it('ranks the open returns by reason in the returns screen words', async () => {
    const element = await open(OPS);

    expect([...element.querySelectorAll('table tbody th')].map((cell) => cell.textContent?.trim())).toEqual([
      '<en returns.reason.Damaged>', '<en returns.reason.WrongSize>',
    ]);
  });

  it('says there is nothing open rather than drawing no bars', async () => {
    const element = await open({ ...OPS, openReturns: 0, openReturnsByReason: [] });

    expect(element.querySelector('backoffice-bar-chart')).toBeNull();
  });
```

Replace the `<en …>` placeholders with the real English words, read from `en.json` at the key `reasonKey()` returns in `operations-widget.ts` today. Move that function into `returns-widget.ts` unchanged.

`stock-widget.spec.ts`:

```ts
  it('counts what to restock and what is sold out from the server counts, and lists the lowest first', async () => {
    const element = await open({
      rows: [{ sku: 'B', available: 2 }, { sku: 'A', available: 1 }, { sku: 'B', available: 1 }],
      counts: [{ filter: 'Restock', count: 5 }, { filter: 'SoldOut', count: 2 }, { filter: 'All', count: 100 }],
      total: 3, page: 1, pageSize: 20, reservations: [], restockAt: 5,
    });

    expect([...element.querySelectorAll('.fact__value')].map((value) => value.textContent?.trim())).toEqual(['5', '2']);
    expect([...element.querySelectorAll('.ranking__name')].map((name) => name.textContent?.trim())).toEqual(['A', 'B']);
  });
```

- [ ] **Step 2: Run and watch them fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — missing `./returns-widget`, `./stock-widget`.

- [ ] **Step 3: Implement**

- `OrdersByStatusWidget` (prefix `dashboard.ordersByStatus`): the first `<dl class="facts">` of `operations-widget.html` (to ship with its late tag, being confirmed, payment failed, cancelled). Each label keeps its `routerLink` to the filtered orders list. At M, a rows `BarChart` of the same four counts: `<div class="at-m"><backoffice-bar-chart layout="rows" …/></div>`. Categories come from the four labels via `translateObjectSignal('dashboard.ordersByStatus')`, one series `{ key: 'orders', role: 'data' }`.
- `HealthWidget` (prefix `dashboard.health`, S): the cancellation fact with its standing tag, moved as-is from `operations-widget.html`, plus the `cancelled` note. It is the one widget with no chart: its figure against its target *is* the reading, and the standing is said in a word and a colour (existing tags).
- `ReturnsWidget` (prefix `dashboard.returns`): open, return rate and refunded as `facts`. At M, a rows chart of `openReturnsByReason` in the returns screen's words, with `reasonKey()` moved from `operations-widget.ts`. With no open returns it shows the `nothingOpen` sentence and no chart.
- `StockWidget` (prefix `dashboard.stock`): reads `sources.stock(culture, at)`. The facts are *To restock* (count `Restock`) and *Sold out* (count `SoldOut`), linking to `/stock` with `queryParams: { filter: 'Restock' | 'SoldOut' }` (check the stock screen's query-parameter name in `features/stock/` and use it). At M it adds `bySkuLowestFirst(rows).slice(0, 5)` as a `ranking` list with `{{count}} left`.
- `TasksWidget`: `stock` now reads `sources.stock(culture, at)`, and every resource's params carry `at: this.refresh.at()`.
- `FunnelWidget`: reads `sources.funnel(period, at)`. Its "Buyers" section stays until Task 7.
- `dashboard-page`: replace `OperationsWidget` with the four new widgets. Delete `operations-widget.*`.

i18n — insert beside `dashboard.operations` in en and es. Move the words used above from `dashboard.operations.*` into their widget's block, keeping each text exactly, then delete `dashboard.operations` once nothing reads it:

| block | keys (new words in **bold**) |
|---|---|
| `dashboard.ordersByStatus` | **title** "Orders by status" / "Pedidos por estado", **toOrders**, loading, failed, toShip, late, inProgress, paymentFailed, **cancelledCount** "Cancelled" / "Cancelados" |
| `dashboard.health` | **title** "Health" / "Salud", loading, failed, cancellationRate, cancelled, standing.{within,near,outside} |
| `dashboard.returns` | **title** "Returns" / "Devoluciones", loading, failed, openReturns, returnRate, refunded, byReason, **nothingOpen** "No returns open." / "No hay devoluciones abiertas.", **toReturns** "Returns" / "Devoluciones" |
| `dashboard.stock` | **title** "Stock" / "Stock", **loading**, **failed**, **restock** "To restock" / "Por reponer", **soldOut** "Sold out" / "Agotado", **lowest** "Lowest first" / "Los más bajos primero", **available** "{{count}} left" / "quedan {{count}}", **toStock** "Stock" / "Stock" |

- [ ] **Step 4: Run and watch them pass**

Run: `cd frontend && npx nx run-many -t lint test -p backoffice shared-i18n --skip-nx-cache`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A frontend/apps/backoffice
git commit -m "feat(backoffice): orders by status, health, returns and stock as widgets of their own (R-9)

The operations panel carried three answers; the Operations tab names them
apart, with the stock beside them. Returns rank their reasons in rows, in
the returns screen's own words; health stays a figure against its target,
said in a word as well as a colour; stock counts what to restock and what is
sold out from Inventory's own counts."
```

---

### Task 7: Customers, what agents buy and what the shop refused them

The funnel's "Buyers" section becomes the Growth tab's *Customers* widget. The agents panel splits into its two answers, each a widget of the Agents tab.

**Files:**
- Create: `features/dashboard/customers-widget.{ts,html,css}`, `agent-sales-widget.{ts,html,css}`, `refusals-widget.{ts,html,css}`, `customers-widget.spec.ts`, `refusals-widget.spec.ts`
- Modify: `funnel-widget.{ts,html}` (loses Buyers), `dashboard-page.{ts,html}`, i18n
- Delete: `agents-widget.{ts,html,css}`

**Interfaces:**
- Consumes: `DashboardSources.funnel|agentSales|agentDenials`, `BarChart` (`share`, `rows`).
- Produces: `CustomersWidget` (`backoffice-customers-widget`, input `period`), `AgentSalesWidget` (`backoffice-agent-sales-widget`, input `period`), `RefusalsWidget` (`backoffice-refusals-widget`, input `period`).

- [ ] **Step 1: Write the failing tests**

`customers-widget.spec.ts`:

```ts
  it('splits the buyers into new and returning, as figures and as one share', async () => {
    const element = await open({ newCustomers: 6, returningCustomers: 2 });

    expect([...element.querySelectorAll('dd.numeric')].map((value) => value.textContent?.trim())).toEqual(['6', '2']);
    expect([...element.querySelectorAll('table td')].map((cell) => cell.textContent?.trim())).toEqual(['6', '2']);
  });

  it('says nobody bought rather than drawing a share of nobody', async () => {
    expect((await open({ newCustomers: 0, returningCustomers: 0 })).querySelector('backoffice-bar-chart')).toBeNull();
  });
```

`refusals-widget.spec.ts`:

```ts
  it('ranks the refusals by rule in rows and keeps the latest with their agent', async () => {
    const element = await open({
      total: 3,
      byReason: [{ reason: 'mandate_required', count: 2 }, { reason: 'orders_per_hour', count: 1 }],
      recent: [{ at: '2026-10-12T10:00:00Z', agentId: 'agent-7', reason: 'mandate_required', detail: null }],
    });

    expect([...element.querySelectorAll('table tbody th')].map((cell) => cell.textContent?.trim())).toEqual(['No mandate', 'Too many orders an hour']);
    expect(element.querySelector('.latest__row')?.textContent).toContain('agent-7');
  });
```

Check the exact `AgentDenialsSummary` field names with `node tools/api-map.mjs --schema AgentDenialsSummary` before writing the fixture.

- [ ] **Step 2: Run and watch them fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — missing modules.

- [ ] **Step 3: Implement**

- `CustomersWidget` (prefix `dashboard.customers`): the new and returning `split` moved from the funnel, plus at M a share `BarChart`. Its categories are `[words.buyers]` and its series are `new` (role `data`) and `returning` (role `previous`, the muted colour: returning is the base, new is the change). The labels come from `translateObjectSignal`. With 0 buyers, the `nobody` sentence. The funnel's `noVisits` note moves here too, since it explains why visits are absent.
- `AgentSalesWidget` (prefix `dashboard.agentSales`): the first `<section>` of `agents-widget.html` moved (share of orders, of sales, mandated, rehearsals, by agent), plus the `toRegistry` link.
- `RefusalsWidget` (prefix `dashboard.refusals`): the second `<section>`. `byReason` becomes a rows `BarChart` at M, with categories from `rule.<reason>`, still reactive via `translateObjectSignal('dashboard.refusals.rule')`. The `total` and the `latest` list stay text at every size.
- `FunnelWidget`: remove the Buyers `h3` and `dl` and the `noVisits` note.
- `dashboard-page`: replace `AgentsWidget` with `AgentSalesWidget` and `RefusalsWidget`, and add `CustomersWidget`. Delete `agents-widget.*`.

i18n: `dashboard.customers.{title "Customers"/"Clientes", loading, failed, buyers "Buyers"/"Compradores", newCustomers, returningCustomers, nobody "Nobody bought in this period."/"Nadie ha comprado en este periodo.", noVisits}`. `dashboard.agentSales.*` and `dashboard.refusals.*` take the words of `dashboard.agents.*` for their halves, exactly, and add `title` "What agents buy"/"Qué compran los agentes" and "What the shop refused them"/"Qué les ha rechazado la tienda". Delete `dashboard.agents` and the funnel's buyer keys once nothing reads them.

- [ ] **Step 4: Run and watch them pass**

Run: `cd frontend && npx nx run-many -t lint test -p backoffice shared-i18n --skip-nx-cache`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A frontend/apps/backoffice
git commit -m "feat(backoffice): customers, what agents buy and what the shop refused them, as widgets (R-9)

The funnel's buyers become the Growth tab's customers, new against returning
in one share; the agents panel splits into its two answers, each a widget of
the Agents tab, the refusals ranked by rule in rows, still in the words the
language says."
```

---

### Task 8: The Overview and the tabs, with their URLs and the period in them

**Files:**
- Create: `features/dashboard/dashboard-period.ts`, `dashboard-period.spec.ts`, `dashboard-layouts.ts`, `dashboard-layouts.spec.ts`, `dashboard-widget-registry.ts`, `dashboard-tab-page.{ts,html,css}`, `dashboard-page.spec.ts`
- Modify: `dashboard-page.{ts,html,css}` (becomes the shell), `app/app.routes.ts`, i18n

**Interfaces:**
- Consumes: `DashboardGrid`, `WidgetPlacement`, `DASHBOARD_WIDGETS`, every widget from Tasks 5–7, `SalesByDayWidget`, `FunnelWidget`, `TasksWidget`.
- Produces:

```ts
// dashboard-period.ts
export type PeriodQuery = 'day' | 'week' | 'month';
export function periodFromQuery(value: string | null): DashboardPeriod;   // any case; unknown or missing → 'Week'
export function queryOfPeriod(period: DashboardPeriod): PeriodQuery;
export function injectDashboardPeriod(): Signal<DashboardPeriod>;         // from the route's ?period=

// dashboard-layouts.ts
export type DashboardTab = 'overview' | 'sales' | 'operations' | 'growth' | 'agents';
export const DashboardTabs: readonly DashboardTab[];                        // in the nav's order
export const DashboardLayouts: Readonly<Record<DashboardTab, readonly WidgetPlacement[]>>;
export function pathOfTab(tab: DashboardTab): string;                       // 'overview' → '', others → their name

// dashboard-widget-registry.ts
export function provideDashboardWidgets(): Provider;                        // DASHBOARD_WIDGETS → the real components
```

The layouts, from the spec's tables and this plan's data limit (a widget whose data is Plan 3's is absent, not faked):

```ts
export const DashboardLayouts = {
  overview: [
    { widget: 'keyFigures', size: 'L' },
    { widget: 'salesByDay', size: 'M' }, { widget: 'waiting', size: 'S' }, { widget: 'health', size: 'S' },
    // Campaigns (M) arrives with its baseline in Plan 3, and Live (S) with R-10.
    { widget: 'byChannel', size: 'S' },
  ],
  sales: [
    { widget: 'keyFigures', size: 'L' },
    { widget: 'salesByDay', size: 'L' },
    { widget: 'byChannel', size: 'M' }, { widget: 'bestSellers', size: 'M' },
    // By category arrives with the category on the order line, in Plan 3.
  ],
  operations: [
    { widget: 'ordersByStatus', size: 'M' }, { widget: 'health', size: 'S' }, { widget: 'funnel', size: 'S' },
    { widget: 'returns', size: 'M' }, { widget: 'stock', size: 'S' }, { widget: 'waiting', size: 'S' },
  ],
  growth: [
    // Campaigns against their baseline and Search's status arrive in Plan 3.
    { widget: 'customers', size: 'M' },
  ],
  agents: [
    { widget: 'agentSales', size: 'M' }, { widget: 'refusals', size: 'M' },
  ],
} as const satisfies Record<DashboardTab, readonly WidgetPlacement[]>;
```

**Rulings to ledger:**
- Growth has one widget until Plan 3. The tab stays, since the spec draws it and its URL should not appear later. Cost if wrong: one thin tab for one plan.
- The Search NDCG widget is not built: `/api/search/evaluation` runs the golden set on every call (`elapsedSeconds`), which a dashboard that refreshes every minute must not do. Plan 3 gives it a stored last result. Cost if wrong: one widget later.

- [ ] **Step 1: Write the failing tests**

`dashboard-period.spec.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { periodFromQuery, queryOfPeriod } from './dashboard-period';

describe('the period in the URL', () => {
  it('reads day, week and month in any case', () => {
    expect(['day', 'WEEK', 'Month'].map(periodFromQuery)).toEqual(['Day', 'Week', 'Month']);
  });

  /** Review focus 1: a stale or odd link still opens the dashboard, on its default week. */
  it('opens on the week when the URL has no period or one it does not know', () => {
    expect([null, '', 'year', '7'].map(periodFromQuery)).toEqual(['Week', 'Week', 'Week', 'Week']);
  });

  it('writes the period in lowercase, as a monitor keeps it', () => {
    expect((['Day', 'Week', 'Month'] as const).map(queryOfPeriod)).toEqual(['day', 'week', 'month']);
  });
});
```

`dashboard-layouts.spec.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { DashboardLayouts, DashboardTabs } from './dashboard-layouts';

const Columns = { S: 3, M: 6, L: 12 } as const;

describe('the dashboards as data', () => {
  it('has the five tabs, the Overview first', () => {
    expect(DashboardTabs).toEqual(['overview', 'sales', 'operations', 'growth', 'agents']);
  });

  it('opens the Overview with the key figures across the whole width', () => {
    expect(DashboardLayouts.overview[0]).toEqual({ widget: 'keyFigures', size: 'L' });
  });

  it('places no widget twice on one tab', () => {
    for (const tab of DashboardTabs) {
      const widgets = DashboardLayouts[tab].map((placement) => placement.widget);
      expect(new Set(widgets).size, tab).toBe(widgets.length);
    }
  });

  /** The Overview "fits one screen at 1440" (spec): its full rows are whole rows of twelve. */
  it('fills the Overview rows it starts', () => {
    const columns = DashboardLayouts.overview.map((placement) => Columns[placement.size]);
    expect(columns.slice(0, 4).reduce((sum, width) => sum + width, 0)).toBe(24);
  });
});
```

`dashboard-page.spec.ts` (RouterTestingHarness; the routes as in `app.routes.ts` but with `DASHBOARD_WIDGETS` provided as stubs that print their key and period, like `dashboard-grid.spec.ts`; `provideTenderoI18n`, flush translations empty):

```ts
  it('opens the tab its URL names, on the period its URL names', async () => {
    const harness = await RouterTestingHarness.create('/dashboard/sales?period=month');
    const element = harness.routeNativeElement as HTMLElement;

    expect(element.querySelector('nav.tabs a[aria-current="page"]')?.getAttribute('href')).toBe('/dashboard/sales?period=month');
    expect(element.querySelector('.period[aria-pressed="true"]')?.getAttribute('data-period')).toBe('Month');
    expect([...element.querySelectorAll('.cell')].map((cell) => (cell as HTMLElement).dataset['widget'])).toEqual([
      'keyFigures', 'salesByDay', 'byChannel', 'bestSellers',
    ]);
  });

  it('keeps the tab when the period changes, and the period when the tab changes', async () => {
    const harness = await RouterTestingHarness.create('/dashboard/operations');
    const element = harness.routeNativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('.period[data-period="Day"]')?.click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/dashboard/operations?period=day');

    element.querySelector<HTMLAnchorElement>('nav.tabs a[href^="/dashboard/agents"]')?.click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/dashboard/agents?period=day');
  });
```

(`harness.routeNativeElement` is the shell, since the dashboard route is the shell with child routes. If the harness exposes the child instead, use `harness.fixture.nativeElement` and ledger it.)

- [ ] **Step 2: Run and watch them fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — missing `./dashboard-period`, `./dashboard-layouts`, and the page has no `nav.tabs`.

- [ ] **Step 3: Implement**

`dashboard-period.ts`:

```ts
import { inject, type Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import type { DashboardPeriod } from '../../data-access/ordering.service';

export type PeriodQuery = 'day' | 'week' | 'month';

const ByQuery: Readonly<Record<PeriodQuery, DashboardPeriod>> = { day: 'Day', week: 'Week', month: 'Month' };

/**
 * The period a URL names (spec: "it is in the URL, so a monitor keeps its
 * period"). Read in any case, as ADR 0043 reads a closed set; anything else is
 * the default week, because a link to a dashboard should open one.
 */
export function periodFromQuery(value: string | null): DashboardPeriod {
  return ByQuery[(value ?? '').toLowerCase() as PeriodQuery] ?? 'Week';
}

export function queryOfPeriod(period: DashboardPeriod): PeriodQuery {
  return period.toLowerCase() as PeriodQuery;
}

/** The dashboard's period, from the current route's query: the shell and every tab read the same one. */
export function injectDashboardPeriod(): Signal<DashboardPeriod> {
  return toSignal(inject(ActivatedRoute).queryParamMap.pipe(map((query) => periodFromQuery(query.get('period')))), { requireSync: true });
}
```

`dashboard-widget-registry.ts`:

```ts
import type { Provider } from '@angular/core';
import { AgentSalesWidget } from './agent-sales-widget';
// ... every widget import
import { DASHBOARD_WIDGETS, type WidgetDefinition, type WidgetKey } from './dashboard-widgets';

/** Which component each widget key is (R-9): the one place a new widget is registered. */
export function provideDashboardWidgets(): Provider {
  const widgets: Record<WidgetKey, WidgetDefinition> = {
    keyFigures: { component: KeyFiguresWidget, takesPeriod: true },
    salesByDay: { component: SalesByDayWidget, takesPeriod: true },
    bestSellers: { component: BestSellersWidget, takesPeriod: true },
    byChannel: { component: ByChannelWidget, takesPeriod: true },
    waiting: { component: TasksWidget, takesPeriod: false },
    health: { component: HealthWidget, takesPeriod: true },
    ordersByStatus: { component: OrdersByStatusWidget, takesPeriod: true },
    funnel: { component: FunnelWidget, takesPeriod: true },
    returns: { component: ReturnsWidget, takesPeriod: true },
    stock: { component: StockWidget, takesPeriod: false },
    customers: { component: CustomersWidget, takesPeriod: true },
    agentSales: { component: AgentSalesWidget, takesPeriod: true },
    refusals: { component: RefusalsWidget, takesPeriod: true },
  };
  return { provide: DASHBOARD_WIDGETS, useValue: widgets };
}
```

`dashboard-tab-page.ts`: reads `tab` from `ActivatedRoute.data` (`toSignal(route.data.pipe(map((data) => data['tab'] as DashboardTab)), { requireSync: true })`) and `period` from `injectDashboardPeriod()`. Its template is `<backoffice-dashboard-grid [placements]="layout()" [period]="period()" [fill]="wall()" />`, where `wall` comes from Task 9 (`inject(FullScreen).active`) and is `false` until then.

`dashboard-page.ts` (the shell): `providers: [DashboardSources, DashboardRefresh]`, imports `RouterLink`, `RouterLinkActive`, `RouterOutlet`, `TranslocoDirective`. It has `period = injectDashboardPeriod()`, `tabs = DashboardTabs`, `pathOfTab`, `queryOfPeriod`, and:

```ts
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected choose(period: DashboardPeriod): void {
    void this.router.navigate([], { relativeTo: this.route.firstChild ?? this.route, queryParams: { period: queryOfPeriod(period) }, queryParamsHandling: 'merge' });
  }
```

`dashboard-page.html`:

```html
<section class="dashboard" *transloco="let t">
  <header class="page-head">
    <h1 class="page-title">{{ t('dashboard.title') }}</h1>
    <div class="page-actions">
      <div class="periods" role="group" [attr.aria-label]="t('dashboard.period.label')">
        @for (option of periods; track option) {
          <button type="button" class="button button--small button--quiet period" [attr.data-period]="option"
            [class.period--active]="option === period()" [attr.aria-pressed]="option === period()" (click)="choose(option)">
            {{ t('dashboard.period.' + option) }}
          </button>
        }
      </div>
    </div>
  </header>
  <nav class="tabs" [attr.aria-label]="t('dashboard.tabs.label')">
    @for (tab of tabs; track tab) {
      <a class="tab" [routerLink]="['/dashboard', pathOfTab(tab)]" [queryParams]="{ period: queryOfPeriod(period()) }"
        routerLinkActive="tab--active" [routerLinkActiveOptions]="{ exact: tab === 'overview' }" ariaCurrentWhenActive="page">
        {{ t('dashboard.tabs.' + tab) }}
      </a>
    }
  </nav>
  <router-outlet />
</section>
```

`pathOfTab('overview')` is `''`, and `['/dashboard', '']` resolves to `/dashboard`: check the rendered `href` in the spec. If Angular renders `/dashboard/`, use `tab === 'overview' ? ['/dashboard'] : ['/dashboard', tab]` and ledger it.

`dashboard-page.css`: keep `.periods`/`.period--active`, drop `.grid*` (now the grid's). Tabs:

```css
.tabs {
  display: flex;
  gap: var(--space-1);
  margin-block-end: var(--space-4);
  border-block-end: 1px solid var(--border);
  /* On a phone the tabs scroll sideways rather than wrap (spec, the phone). */
  overflow-x: auto;
  scrollbar-width: thin;
}

.tab {
  flex: none;
  display: inline-flex;
  align-items: center;
  min-block-size: 2.75rem;
  padding-inline: var(--space-3);
  color: var(--text-muted);
  text-decoration: none;
  border-block-end: 2px solid transparent;
  white-space: nowrap;
}

.tab:hover { color: var(--text); }
.tab--active { color: var(--text); border-block-end-color: var(--text); font-weight: 600; }
```

`app.routes.ts` — the dashboard route:

```ts
      {
        path: 'dashboard',
        canActivate: [shopkeeper],
        providers: [provideDashboardWidgets()],
        loadComponent: () => import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
        children: DashboardTabs.map((tab) => ({
          path: pathOfTab(tab),
          title: `dashboard.tabTitles.${tab}`,
          data: { tab },
          loadComponent: () => import('./features/dashboard/dashboard-tab-page').then((m) => m.DashboardTabPage),
        })),
      },
```

Importing `provideDashboardWidgets`, `DashboardTabs` and `pathOfTab` statically into `app.routes.ts` pulls every widget into `main`. **Don't.** Instead:
- Move `DashboardTabs` and `pathOfTab` into a tiny `dashboard-tabs.ts` with no widget import, and let `dashboard-layouts.ts` re-export them.
- Provide the registry from the shell component's `providers` rather than the route's.
- Build the page's spec with a component-level override: `TestBed.overrideComponent(DashboardPage, { add: { providers: [{ provide: DASHBOARD_WIDGETS, useValue: stubs }] } })`.

Check `npx nx build backoffice`: the initial total must stay within 1 KB of Task 3's.

i18n: `dashboard.tabs.{label "Dashboards"/"Paneles", overview "Overview"/"Resumen", sales "Sales"/"Ventas", operations "Operations"/"Operaciones", growth "Growth"/"Crecimiento", agents "Agents"/"Agentes"}` and `dashboard.tabTitles.{overview "Dashboard"/"Panel", sales "Sales · Dashboard"/"Ventas · Panel", operations "Operations · Dashboard"/"Operaciones · Panel", growth "Growth · Dashboard"/"Crecimiento · Panel", agents "Agents · Dashboard"/"Agentes · Panel"}`. Check `translated-titles.ts` and its spec for how route titles are keyed and resolved, and follow it.

`usage.ts`'s `viewOf('/dashboard/sales')` still says `dashboard` (first segment). The per-tab view is Plan 4's event work (D-8). Leave it and ledger that it is left.

- [ ] **Step 4: Run and watch them pass; build**

Run: `cd frontend && npx nx run-many -t lint test -p backoffice shared-i18n --skip-nx-cache && npx nx build backoffice`
Expected: PASS; initial total within 1 KB of the previous task's.

- [ ] **Step 5: Commit**

```bash
git add -A frontend/apps/backoffice
git commit -m "feat(backoffice): the Overview and four tabs, each with its URL and the period in it (R-9)

/dashboard, /dashboard/sales, /operations, /growth and /agents, each a list
of placements drawn by the grid, and ?period=day|week|month read in any case
and written lowercase, so a monitor keeps its tab and its period. A widget
whose data is Plan 3's is absent rather than faked: the Overview's campaigns,
the sales by category, Growth's campaigns and search."
```

---

### Task 9: Full screen — the button, the wall and the minute's refresh

**Files:**
- Create: `features/dashboard/full-screen.ts`, `full-screen.spec.ts`, `dashboard-refresh.spec.ts`
- Modify: `dashboard-refresh.ts` (the minute's tick), `dashboard-page.{ts,html,css}`, `dashboard-page.spec.ts`, `dashboard-tab-page.ts` (`fill`), `dashboard-grid.css` (fill), `apps/backoffice/src/styles.css` (the wall's type), i18n

**Interfaces:**
- Consumes: `DashboardRefresh`, `DashboardGrid.fill`, `DashboardTabs`, `injectDashboardPeriod`.
- Produces:

```ts
// full-screen.ts
@Injectable({ providedIn: 'root' })
export class FullScreen {
  readonly supported: boolean;               // document.fullscreenEnabled
  readonly active: Signal<boolean>;          // follows fullscreenchange — Esc included
  enter(element: Element): Promise<void>;
  leave(): Promise<void>;
}
// dashboard-refresh.ts gains
  every(milliseconds: number): void;         // starts ticking; a second call replaces the first
  stop(): void;
```

- [ ] **Step 1: Write the failing tests**

`dashboard-refresh.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DashboardRefresh } from './dashboard-refresh';

describe('the dashboard refresh', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [DashboardRefresh] });
  });
  afterEach(() => vi.useRealTimers());

  it('asks again every minute while it ticks, and not after it stops', () => {
    const refresh = TestBed.inject(DashboardRefresh);
    const first = refresh.at();
    refresh.every(60_000);
    vi.advanceTimersByTime(60_000);
    const second = refresh.at();
    refresh.stop();
    vi.advanceTimersByTime(180_000);

    expect([second > first, refresh.at()]).toEqual([true, second]);
  });

  it('stops ticking when the dashboard goes', () => {
    const refresh = TestBed.inject(DashboardRefresh);
    refresh.every(60_000);
    const at = refresh.at();
    TestBed.resetTestingModule();
    vi.advanceTimersByTime(120_000);

    expect(refresh.at()).toBe(at);
  });
});
```

`full-screen.spec.ts`:

```ts
import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { FullScreen } from './full-screen';

describe('full screen', () => {
  /** Review focus 3: Esc is the browser's, not ours — the state follows its event, whoever left. */
  it('follows the browser in and out, Esc included', () => {
    const document = TestBed.inject(DOCUMENT);
    const fullScreen = TestBed.inject(FullScreen);
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: document.body });
    document.dispatchEvent(new Event('fullscreenchange'));
    const inside = fullScreen.active();
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: null });
    document.dispatchEvent(new Event('fullscreenchange'));

    expect([inside, fullScreen.active()]).toEqual([true, false]);
  });
});
```

`dashboard-page.spec.ts` gains a fake `FullScreen` (`{ supported: true, active: signal(false), enter: vi.fn(async () => active.set(true)), leave: vi.fn(async () => active.set(false)) }`), provided for the test:

```ts
  it('puts its own section on the screen, and shows the tab, the period, the time and the way out there', async () => {
    const harness = await RouterTestingHarness.create('/dashboard/operations?period=day');
    const element = harness.routeNativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('.full-screen-enter')?.click();
    harness.detectChanges();

    expect(fake.enter).toHaveBeenCalledWith(element.querySelector('section.dashboard'));
    expect(element.querySelector('.wall-head')?.textContent).toContain('<en dashboard.tabs.operations>');
    expect(element.querySelector('.wall-head')?.textContent).toContain('<en dashboard.period.Day>');
    expect(element.querySelector('.wall-head .full-screen-leave')).not.toBeNull();
  });

  it('refreshes every minute on the wall, and only there', async () => {
    vi.useFakeTimers();
    const harness = await RouterTestingHarness.create('/dashboard');
    const refresh = harness.fixture.debugElement.query(By.directive(DashboardPage)).injector.get(DashboardRefresh);
    const before = refresh.at();
    vi.advanceTimersByTime(120_000);
    expect(refresh.at()).toBe(before);

    fake.active.set(true);
    harness.detectChanges();
    vi.advanceTimersByTime(60_000);
    expect(refresh.at()).toBeGreaterThan(before);
    vi.useRealTimers();
  });

  /** Review focus 3: leaving by Esc is the browser's event; the page comes back and the tick stops. */
  it('leaves the wall when the browser does', async () => {
    const harness = await RouterTestingHarness.create('/dashboard');
    fake.active.set(true);
    harness.detectChanges();
    fake.active.set(false);
    harness.detectChanges();

    expect((harness.routeNativeElement as HTMLElement).querySelector('.wall-head')).toBeNull();
  });
```

Use the real English words in place of the `<en …>` placeholders, by flushing the real `en.json` as the widget specs do.

- [ ] **Step 2: Run and watch them fail**

Run: `cd frontend && npx nx test backoffice --skip-nx-cache`
Expected: FAIL — `every` is not a function, `./full-screen` is missing, and the page has no `.full-screen-enter`.

- [ ] **Step 3: Implement**

`dashboard-refresh.ts` gains:

```ts
  private tick: ReturnType<typeof setInterval> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  /** Until R-10's live source, a wall asks again on a clock (spec: "refreshes every minute, and says so"). */
  every(milliseconds: number): void {
    this.stop();
    this.tick = setInterval(() => this.refresh(), milliseconds);
  }

  stop(): void {
    clearInterval(this.tick);
    this.tick = undefined;
  }
```

`full-screen.ts`:

```ts
import { DestroyRef, DOCUMENT, inject, Injectable, signal } from '@angular/core';

/**
 * The browser's Fullscreen API as a signal (R-9): whether the page is on a
 * wall, whoever put it there or took it off — the button, or Esc, which is the
 * browser's and never reaches us as a key.
 */
@Injectable({ providedIn: 'root' })
export class FullScreen {
  private readonly document = inject(DOCUMENT);
  private readonly inside = signal(this.document.fullscreenElement != null);

  readonly supported = this.document.fullscreenEnabled ?? false;
  readonly active = this.inside.asReadonly();

  constructor() {
    const follow = () => this.inside.set(this.document.fullscreenElement != null);
    this.document.addEventListener('fullscreenchange', follow);
    inject(DestroyRef).onDestroy(() => this.document.removeEventListener('fullscreenchange', follow));
  }

  enter(element: Element): Promise<void> {
    return element.requestFullscreen();
  }

  leave(): Promise<void> {
    return this.document.fullscreenElement ? this.document.exitFullscreen() : Promise.resolve();
  }
}
```

`dashboard-page.ts`: inject `FullScreen` and `DashboardRefresh`; `@ViewChild`-free with `viewChild.required<ElementRef<HTMLElement>>('wall')`. Then:

```ts
  protected readonly fullScreen = inject(FullScreen);
  private readonly refresh = inject(DashboardRefresh);
  private readonly wall = viewChild.required<ElementRef<HTMLElement>>('wall');
  protected readonly tab = /* the active child's data['tab'], via router events → toSignal */;
  protected readonly updated = computed(() => formatTime(this.refresh.at(), this.culture.active()));

  constructor() {
    effect(() => (this.fullScreen.active() ? this.refresh.every(60_000) : this.refresh.stop()));
  }

  protected enterWall(): void { void this.fullScreen.enter(this.wall().nativeElement); }
  protected leaveWall(): void { void this.fullScreen.leave(); }
```

`formatTime`: use `@tendero/shared-util`'s time formatter if one exists (grep `formatTime|formatDateTime`). Otherwise use `new Intl.DateTimeFormat(culture, { timeStyle: 'short' }).format(at)` inline, since that is presentation. `tab`: read it from the router, `toSignal(router.events.pipe(filter(e => e instanceof NavigationEnd), startWith(null), map(() => deepest child of route snapshot's data['tab'] as DashboardTab)))`. Keep it in a small function in `dashboard-page.ts`.

`dashboard-page.html`: `<section class="dashboard" #wall …>`. Add inside `page-actions`, after the periods:

```html
      @if (fullScreen.supported) {
        <button type="button" class="button button--small button--quiet full-screen-enter" (click)="enterWall()">{{ t('dashboard.fullScreen.enter') }}</button>
      }
```

and, first inside the section:

```html
  @if (fullScreen.active()) {
    <header class="wall-head">
      <h1 class="wall-head__title">{{ t('dashboard.tabs.' + tab()) }}</h1>
      <span>{{ t('dashboard.period.' + period()) }}</span>
      <span class="muted">{{ t('dashboard.fullScreen.updated', { time: updated() }) }} · {{ t('dashboard.fullScreen.everyMinute') }}</span>
      <button type="button" class="button button--small button--quiet full-screen-leave" (click)="leaveWall()">{{ t('dashboard.fullScreen.leave') }}</button>
    </header>
  }
```

and wrap the normal `page-head` and `nav.tabs` in `@if (!fullScreen.active())`.

`dashboard-page.css`:

```css
/* The button is for a monitor: from 1440 up, never on a phone (spec, full screen). */
.full-screen-enter { display: none; }
@media (min-width: 90rem) { .full-screen-enter { display: inline-flex; } }

/* On the wall the section is the whole screen: its own background, its rows
   sharing the height, the head reduced to what a glance from across a room needs. */
.dashboard:fullscreen {
  display: flex;
  flex-direction: column;
  block-size: 100dvh;
  padding: var(--space-5);
  overflow: auto;
  background: var(--bg-page);
}

.dashboard:fullscreen > :last-child { flex: 1; min-block-size: 0; }

.wall-head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: var(--space-2) var(--space-5);
  margin-block-end: var(--space-4);
}

.wall-head__title { margin: 0; font-size: var(--text-xl); }
.full-screen-leave { margin-inline-start: auto; }
```

`dashboard-tab-page`: `fill = inject(FullScreen).active`, passed to the grid, and `:host { display: block; block-size: 100%; }`.

`dashboard-grid.css`:

```css
/* On a wall (R-9): the grid takes the screen's height, its rows share it, and
   each widget spreads its chart over the height it is given. */
.grid--fill {
  block-size: 100%;
  grid-auto-rows: minmax(0, 1fr);
  padding-block-end: 0;
}

.grid--fill .cell > * { display: flex; flex-direction: column; }
.grid--fill { --chart-block-size: 100%; }
```

Then in `bar-chart.css`, so a chart can fill a widget that has height: `:host { display: block; flex: 1 1 auto; min-block-size: 0; } .chart { block-size: 100%; grid-template-rows: auto minmax(8rem, 1fr) auto; }`. The plot's `block-size: var(--chart-block-size)` becomes `100%` only under `.grid--fill`, and ECharts' ResizeObserver redraws.

`styles.css`:

```css
/* A wall is read from further away (spec, full screen): the type — and every
   rem, including the size contract's thresholds — grows with the monitor,
   from the ordinary 16px at 1440 to 24px at most. */
html:has(.dashboard:fullscreen) {
  font-size: clamp(100%, 0.25rem + 0.85vw, 150%);
}
```

i18n `dashboard.fullScreen.{enter "Full screen"/"Pantalla completa", leave "Leave full screen"/"Salir de pantalla completa", updated "Updated at {{time}}"/"Actualizado a las {{time}}", everyMinute "refreshes every minute"/"se actualiza cada minuto"}`.

- [ ] **Step 4: Run and watch them pass**

Run: `cd frontend && npx nx run-many -t lint test -p backoffice shared-i18n --skip-nx-cache`
Expected: PASS.

- [ ] **Step 5: Hand mutation** — in `dashboard-page.ts` replace the effect's body with `this.refresh.every(60_000)` (ticking whether on the wall or not). Run. Expected: "refreshes every minute on the wall, and only there" FAILS. Restore in place, run again: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A frontend/apps/backoffice
git commit -m "feat(backoffice): each dashboard tab goes full screen on a monitor of its own (R-9)

The browser's Fullscreen API on the tab's own section, so there is no second
identity and no token in a URL: on the wall the tab says its name, its
period, when it was updated and the way out; its rows share the screen's
height, the type grows with the monitor, and until R-10 it asks again every
minute. Esc is the browser's, and the page follows its event.

Hand mutation: ticking whether on the wall or not turns \"refreshes every
minute on the wall, and only there\" red."
```

---

### Task 10: The phone, and the browser proves the rest

Container queries, the Fullscreen API and a phone's sideways-scrolling tabs cannot be seen by jsdom. This task holds them in Playwright against the running stack, adds the tabs to the accessibility ratchet in both themes, and files the docs.

**Files:**
- Create: `frontend/apps/backoffice-e2e/src/dashboard.spec.ts`
- Modify: `frontend/apps/backoffice-e2e/src/accessibility.spec.ts`, `docs/specs/backoffice-dashboard.md` (status), `docs/delivery-plan.md` (R-9's line), `docs/blog/notebook.md`, `docs/book/material-map.md`

- [ ] **Step 1: Write the browser spec** — `dashboard.spec.ts`:

```ts
import { expect, test } from '@playwright/test';
import { signIn } from './session';

/**
 * What only a browser can prove about R-9's dashboard: widgets choosing their
 * content from their own width, the tabs' URLs, full screen, and the phone.
 */
test.describe('the dashboard', () => {
  test.beforeEach(async ({ page }) => signIn(page));

  test('keeps its tab and its period in the URL', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto('/dashboard/sales?period=month');
    await expect(page.locator('nav.tabs a[aria-current="page"]')).toHaveAttribute('href', '/dashboard/sales?period=month');
    await page.locator('nav.tabs a', { hasText: 'Operations' }).click();
    await expect(page).toHaveURL(/\/dashboard\/operations\?period=month$/);
  });

  test('a widget shows its figure at S, its chart at M, its chart and table at L', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto('/dashboard');
    const byChannel = page.locator('[data-widget="byChannel"]');           // S on the Overview
    await expect(byChannel.locator('.only-s')).toBeVisible();
    await expect(byChannel.locator('.at-m')).toBeHidden();
    await expect(page.locator('[data-widget="salesByDay"] .figures-toggle')).toBeVisible();   // M: the table behind a toggle

    await page.goto('/dashboard/sales');
    const salesByDay = page.locator('[data-widget="salesByDay"]');       // L on Sales
    await expect(salesByDay.locator('table.figures')).toBeVisible();
    await expect(salesByDay.locator('.figures-toggle')).toBeHidden();
  });

  test('goes full screen on a monitor and comes back with Esc', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto('/dashboard/operations');
    await page.locator('.full-screen-enter').click();
    await expect.poll(() => page.evaluate(() => document.fullscreenElement?.className ?? null)).toContain('dashboard');
    await expect(page.locator('.wall-head')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect.poll(() => page.evaluate(() => document.fullscreenElement)).toBeNull();
    await expect(page.locator('nav.tabs')).toBeVisible();
  });

  test('on a phone: no full screen, no sideways page, the tabs scroll', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/dashboard');
    await expect(page.locator('.full-screen-enter')).toBeHidden();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
    const tabs = await page.locator('nav.tabs').evaluate((nav) => [nav.scrollWidth > nav.clientWidth, getComputedStyle(nav).overflowX]);
    expect(tabs).toEqual([true, 'auto']);
  });
});
```

If headless Chromium refuses `requestFullscreen` or Esc does not leave it, do not weaken the test. Ledger it, keep the button-and-leave half with `.full-screen-leave`, and state in the ledger that Esc is covered by the unit test (`full-screen.spec.ts`) only.

- [ ] **Step 2: The accessibility ratchet** — in `accessibility.spec.ts`, add the tabs to `Screens`:

```ts
  ...['sales', 'operations', 'growth', 'agents'].map((tab) => [`dashboard-${tab}`, `/dashboard/${tab}`] as const),
```

and a second test for the dark theme, on the dashboard's five URLs only (spec: "covers every tab, in both themes"):

```ts
test('the dashboard stays as accessible in the dark theme', async ({ page }) => {
  test.setTimeout(240_000);
  await page.addInitScript(() => localStorage.setItem('tendero.theme', 'dark'));
  await signIn(page);
  await hold(Screens.filter(([name]) => name.startsWith('dashboard')).map(([name, url]) => [`${name}-dark`, url] as const), page);
});
```

`hold` rewrites the baseline file once per test, which would erase the other test's findings under `UPDATE_A11Y_BASELINE`. Make `hold` read the existing baseline and replace only the names it audited. If that is more than a few lines, ledger the choice.

- [ ] **Step 3: Run the stack and the browser specs**

Build the AppHost first (memory: *Aspire runs projects from their own bin*): `dotnet build src/AppHost`. Start `dotnet run --project src/AppHost` in the background, wait for the backoffice URL, then:

Run: `cd frontend && npx nx e2e backoffice-e2e -- --grep "dashboard|accessible"`
Expected: PASS, and the a11y baseline stays `[]`. Any new finding is fixed in the component, never added to the baseline.

Screenshots for the ledger (not the repository): the Overview at 1440 light and dark, Operations full screen at 1920, the Overview at 390. Look at each one. If a threshold of the size contract is wrong on screen (an S widget drawing a chart at 1440, an M widget showing only a figure), change the rem in `styles.css` and ledger the measured widths. **Stop the stack** and remove any container this session started.

- [ ] **Step 4: Docs**
- `docs/specs/backoffice-dashboard.md`: status line, "Plan 2 built (D-3 to D-6)", with what waits for Plan 3 (the Overview's campaigns, by category, Growth's campaigns and search, conversion and return rate trends).
- `docs/delivery-plan.md`: append to R-9's line *Plan 2 built …* in the same voice as Plan 1's sentence.
- `docs/blog/notebook.md`: the shared reads (six widgets, ten requests → one per endpoint), the size contract as container queries in rem (the wall moves the thresholds), Esc belonging to the browser, the Search evaluation that could not go on a refreshing wall.
- `docs/book/material-map.md`: pointers from this plan's commits to the dashboard chapter.

- [ ] **Step 5: Final checks and commit**

Run: `cd frontend && npx nx run-many -t lint test -p backoffice shared-i18n --skip-nx-cache && npx nx build backoffice`
Expected: PASS, no new warning.

```bash
git add -A frontend/apps/backoffice-e2e docs
git commit -m "test(backoffice): the browser holds the size contract, the tabs, full screen and the phone (R-9)

jsdom evaluates neither container queries nor the Fullscreen API: a widget's
figure at S and table at L, a tab's URL keeping its period, a wall entered
and left with Esc, and a phone with no sideways page and tabs that scroll
are held against the running stack. The accessibility ratchet covers every
tab, in both themes, and its baseline stays empty."
```

Push once, at the end (memory: batch pushes to save CI minutes).
