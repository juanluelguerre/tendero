import { contrastOf } from './contrast';

/** What one page, at one width, fails. Each entry names the element, so a finding can be found. */
export interface PageAudit {
  readonly overflow: number;
  readonly overflowing: readonly string[];
  readonly imagesWithoutAlt: readonly string[];
  readonly unnamedControls: readonly string[];
  readonly unlabelledFields: readonly string[];
  readonly smallTargets: readonly string[];
  readonly lowContrast: readonly string[];
}

/** The part of Playwright's `Page` this needs, so the library does not depend on Playwright. */
export interface EvaluatingPage {
  evaluate<R, A>(fn: (arg: A) => R, arg: A): Promise<R>;
}

/**
 * Runs the checks inside the page. Everything the browser executes is in the
 * function below or in `contrastOf`, whose source is handed in, because a
 * function sent to the browser keeps nothing it closed over.
 */
export function auditPage(page: EvaluatingPage): Promise<PageAudit> {
  return page.evaluate(inBrowser, contrastOf.toString());
}

function inBrowser(contrastSource: string): PageAudit {
  const contrast = new Function(`return (${contrastSource})`)() as (f: string, b: string) => number;
  const visible = (el: Element): boolean => {
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none' && Number(s.opacity) > 0;
  };
  const name = (el: Element): string =>
    (el.getAttribute('aria-label') || el.getAttribute('title') || el.textContent || '').trim() ||
    (el.getAttribute('aria-labelledby') ? 'labelledby' : '') ||
    [...el.querySelectorAll('img[alt], svg[aria-label]')].map((i) => i.getAttribute('alt') || i.getAttribute('aria-label')).join('').trim();
  const describe = (el: Element): string => {
    const text = (el.textContent || '').trim().slice(0, 24);
    const classes = typeof el.className === 'string' && el.className.trim() ? '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.') : '';
    return `${el.tagName.toLowerCase()}${classes}${text ? ` "${text}"` : ''}`;
  };
  const backgroundOf = (el: Element | null): string => {
    for (let node = el; node; node = node.parentElement) {
      const bg = getComputedStyle(node).backgroundColor;
      if (bg && !/rgba\([^)]*,\s*0\)$/.test(bg) && bg !== 'transparent') return bg;
      if (getComputedStyle(node).backgroundImage !== 'none') return '';
    }
    return getComputedStyle(document.body).backgroundColor || 'rgb(255, 255, 255)';
  };

  const width = document.documentElement.clientWidth;
  const overflowing: string[] = [];
  if (document.documentElement.scrollWidth > window.innerWidth + 1) {
    for (const el of document.querySelectorAll('body *')) {
      if (!visible(el) || el.closest('.table-wrap')) continue;
      if (el.getBoundingClientRect().right > width + 1) overflowing.push(describe(el));
    }
  }

  const controls = [...document.querySelectorAll('a, button, [role=button], input, select, textarea')].filter(visible);
  const smallTargets = controls
    .filter((el) => {
      const r = el.getBoundingClientRect();
      // Inline links in a sentence are exempt (2.5.8's "inline" exception).
      return (r.width < 24 || r.height < 24) && !(el.tagName === 'A' && el.closest('p, li, td, dd'));
    })
    .map(describe);

  const lowContrast: string[] = [];
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  const seen = new Set<Element>();
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const el = node.parentElement;
    if (!el || seen.has(el) || !(node.textContent || '').trim() || !visible(el)) continue;
    seen.add(el);
    const style = getComputedStyle(el);
    const background = backgroundOf(el);
    if (!background) continue;
    const size = parseFloat(style.fontSize);
    const weight = Number(style.fontWeight) || 400;
    const minimum = size >= 24 || (size >= 18.66 && weight >= 700) ? 3 : 4.5;
    const ratio = contrast(style.color, background);
    if (ratio < minimum) lowContrast.push(`${describe(el)} ${ratio.toFixed(2)}:1 < ${minimum}`);
  }

  return {
    overflow: document.documentElement.scrollWidth - window.innerWidth,
    overflowing: [...new Set(overflowing)].slice(0, 8),
    imagesWithoutAlt: [...document.querySelectorAll('img')].filter((i) => !i.hasAttribute('alt')).map(describe),
    unnamedControls: controls.filter((el) => ['A', 'BUTTON'].includes(el.tagName) && !name(el)).map(describe),
    unlabelledFields: [...document.querySelectorAll('input:not([type=hidden]), select, textarea')]
      .filter(visible)
      .filter((el) => !(el as HTMLInputElement).labels?.length && !el.getAttribute('aria-label') && !el.getAttribute('aria-labelledby') && !el.getAttribute('title'))
      .map(describe),
    smallTargets: [...new Set(smallTargets)],
    lowContrast: [...new Set(lowContrast)],
  };
}
