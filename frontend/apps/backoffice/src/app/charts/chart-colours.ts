import type { SeriesRole } from './chart-contract';
import { roleColourToken } from './chart-contract';

/**
 * The tokens' colours, resolved where the chart sits — the only way a colour
 * reaches ECharts, which draws from JavaScript and cannot read `var(--…)`.
 */
export function readChartColours(element: Element): Record<SeriesRole, string> {
  const style = getComputedStyle(element);
  const read = (role: SeriesRole) => style.getPropertyValue(roleColourToken(role)).trim();
  return { data: read('data'), previous: read('previous'), agent: read('agent') };
}
