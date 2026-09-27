import type { PageAudit } from './audit';

/**
 * One finding as a stable key — the page, the width, the kind and the element
 * without its text — so a baseline does not move when the shop's data does.
 */
export function findingsOf(page: string, width: number, audit: PageAudit): string[] {
  const kinds: [keyof PageAudit, readonly string[]][] = [
    ['overflowing', audit.overflowing],
    ['imagesWithoutAlt', audit.imagesWithoutAlt],
    ['unnamedControls', audit.unnamedControls],
    ['unlabelledFields', audit.unlabelledFields],
    ['smallTargets', audit.smallTargets],
    ['lowContrast', audit.lowContrast],
  ];
  const keys = kinds.flatMap(([kind, elements]) =>
    elements.map((element) => `${page}@${width} ${kind}: ${element.replace(/ "[^"]*"/, '').replace(/ \d+(\.\d+)?:1 < /, ' < ')}`),
  );
  return [...new Set(keys)].sort();
}

/** What a run found that the baseline does not know (regressions), and what the baseline knows that is gone (fixed). */
export function compareToBaseline(found: readonly string[], baseline: readonly string[]): { added: string[]; fixed: string[] } {
  const known = new Set(baseline);
  const now = new Set(found);
  return {
    added: found.filter((key) => !known.has(key)),
    fixed: baseline.filter((key) => !now.has(key)),
  };
}
