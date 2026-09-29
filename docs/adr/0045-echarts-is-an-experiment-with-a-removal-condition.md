# 0045 — ECharts is an experiment with a removal condition

**Status**: accepted (the author, 2026-09-29, with R-9's spec) — **decided the same day: ECharts stays**

## Context

R-9 needs charts, and the author chose to compare two engines rather than pick
one: our own SVG, and Apache ECharts. ECharts 6.1.0 is Apache-2.0 — **tier 1**
under the licensing policy, since it ships inside the backoffice — and weighs
555 KB minified / 192 KB gzip with only bars, line, donut, grid, tooltip, aria
and the SVG renderer (measured 2026-09-29), against about 900 KB for the whole
backoffice.

## Decision

1. `echarts@6.1.0` enters `frontend/package.json` for the comparison, pinned.
   `ngx-echarts` does not: the binding is a directive of ours.
2. It is imported by parts from `echarts/core` and only in a deferred block, so
   it is its own chunk and never in the initial bundle.
3. **The removal condition**: when the comparison on *sales by day* (R-9, Plan 1)
   is decided, the engine that loses is deleted — code, token value and, for
   ECharts, the dependency and its lockfile entries — in the same pull request
   that records the numbers in the spec.

## Consequences

- For the length of the comparison, the backoffice has two ways to draw a bar,
  behind one contract (`backoffice-bar-chart`).
- Colours reach ECharts through JavaScript, read from the tokens at run time; the
  tokens test does not see them. If ECharts stays, a test must hold that no
  colour reaches it except through `readChartColours`.

## Outcome (2026-09-29)

The comparison ran on *sales by day* behind the same contract; the numbers are in
`docs/specs/backoffice-dashboard.md`, *As measured — the chart engines*. Our own SVG
was lighter (2.5 KB against 495 KB minified / 170 KB gzip, and 1.1 KB against 9.7 KB
more in the initial bundle) and faster to redraw (0.08–0.15 ms against 1.9–2.8 ms),
and it was the recommendation.

**The author chose ECharts**: using a mature, widely known library is more
didactic for the series and the book than a chart engine of our own — what it
teaches applies outside Tendero — and it brings the axis with its values, rich
tooltips and every chart type a later dashboard may want, among other pros and
cons the author weighed.

As the removal condition asked, the engine that lost left in the same change: the
SVG engine, its scale, the lab page and the measuring script (the script stays in
the history; its numbers in the spec). `CHART_ENGINE` left with them — a seam with
one side is dead code — and the contract component stays, so a later change of
engine touches no widget.

Colours reach ECharts only from the tokens, read at run time, and the test this ADR
asked for holds it: `barOptions` carries no colour but the ones it is given
(`echarts-bar-options.spec.ts`).

