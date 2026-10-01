import type { SeriesRole } from './chart-contract';
import { roleColourToken } from './chart-contract';

/** Every colour a chart draws with: the series' by role, and the ink, rules and surface around them. */
export interface ChartColours {
  readonly series: Readonly<Record<SeriesRole, string>>;
  readonly text: string;
  readonly muted: string;
  readonly border: string;
  readonly surface: string;
  readonly font: string;
}

/**
 * The tokens' colours, resolved where the chart sits — the only way a colour
 * reaches ECharts, which draws from JavaScript and cannot read `var(--…)`.
 */
export function readChartColours(element: Element): ChartColours {
  const style = getComputedStyle(element);
  const token = (name: string) => style.getPropertyValue(name).trim();
  const role = (one: SeriesRole) => token(roleColourToken(one));
  return {
    series: { data: role('data'), previous: role('previous'), agent: role('agent') },
    text: token('--color-body'),
    muted: token('--color-muted'),
    border: token('--color-hairline'),
    surface: token('--color-surface'),
    font: token('--font-sans'),
  };
}
