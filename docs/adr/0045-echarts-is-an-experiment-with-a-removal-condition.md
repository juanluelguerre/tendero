# 0045 — ECharts is an experiment with a removal condition

**Status**: accepted (the author, 2026-09-29, with R-9's spec)

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
