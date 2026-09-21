import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * The design system's other promise, made executable.
 *
 * `design/DESIGN.md` says "colour never carries meaning alone" and "every
 * interactive element has a visible `:focus-visible` ring. No exceptions." Both
 * are about being able to SEE the interface, and neither had anything checking
 * it — `design-tokens.spec.ts` keeps colours in one file and says nothing about
 * whether those colours can be read on the surfaces they sit on.
 *
 * What that cost, measured on 2026-09-20 (ADR 0034): white on the accent at
 * 3.87:1 was the one clay action of every storefront view, `--warning` was
 * 2.88:1 on `--bg-page`, and `--text-subtle` failed on every surface in BOTH
 * themes across its 38 readers. The worst of them was not a weak ratio but an
 * absent token: a button wrote `color: var(--on-accent)` with nothing defining
 * it, so the declaration was invalid and the colour fell through to the
 * inherited `--text`, giving 2.45:1 on an accent fill.
 *
 * The rule the pass arrived at, and what this test really guards: **a ramp's
 * middle step is a fill, and only its dark step may carry text.** `--clay-500`
 * stays the brand primary for the logo and the awning, where nothing sits on
 * it; the interface's `--accent` is `--clay-600`, which can be read.
 *
 * The threshold is WCAG 2.1 AA: 4.5:1 for body text, and 3:1 for a non-text
 * indicator like the focus ring (1.4.11). Large text gets a lower bar and is
 * deliberately not modelled for the semantic tokens — this system puts
 * `--text-3xs` (11px) on almost every one of them, so the body bar is the honest
 * one. The single exception is the brand: the two wordmarks are set in
 * `--brand`, and they are held to the large-text bar only because a test below
 * also holds their size.
 */
describe('the design tokens, read on the surfaces they sit on', () => {
  const SOURCE = 'design/tokens.css';

  /** AA for body text, for large text (1.4.3), and for a non-text indicator (1.4.11). */
  const TEXT_BAR = 4.5;
  const LARGE_TEXT_BAR = 3;
  const INDICATOR_BAR = 3;

  /** WCAG's large text: 18pt, or 14pt bold — 24px, or 18.66px at 700 and above. */
  const LARGE_BOLD_MIN_PX = 18.66;
  const ROOT_FONT_PX = 16;

  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, SOURCE))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${SOURCE} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  /** Comments name colours in prose; the rule is about values. */
  const css = readFileSync(join(repository, SOURCE), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

  function declarations(pattern: RegExp): Record<string, string> {
    const body = css.match(pattern)?.[1] ?? '';

    return Object.fromEntries(
      [...body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]),
    );
  }

  const root = declarations(/:root\s*\{([\s\S]*?)\n\}/);
  const dark = declarations(/\[data-theme='dark'\]\s*\{([\s\S]*?)\n\}/);

  /**
   * A theme is the root palette with the dark block laid over it, because that
   * is what the browser computes: `data-theme` goes on the root element, so a
   * token the dark block leaves alone keeps its `:root` value, and an alias
   * like `--focus-ring: var(--accent)` resolves against whichever `--accent`
   * won on that element.
   */
  const themes: Record<string, Record<string, string>> = {
    light: root,
    dark: { ...root, ...dark },
  };

  /** Follows `var(--x)` until it reaches a literal, so aliases are measured. */
  function resolve(theme: Record<string, string>, token: string, seen = new Set<string>()): string {
    const value = theme[token];
    if (value === undefined) throw new Error(`${token} is used but never defined`);

    const alias = value.match(/^var\(\s*(--[\w-]+)\s*\)$/);
    if (alias) {
      if (seen.has(token)) throw new Error(`${token} resolves in a circle`);
      seen.add(token);

      return resolve(theme, alias[1], seen);
    }

    if (!/^#[0-9a-f]{6}$/i.test(value)) throw new Error(`${token} is not a six-digit hex: ${value}`);

    return value.toUpperCase();
  }

  /** WCAG 2.1 relative luminance, and the contrast ratio built from it. */
  function contrast(foreground: string, background: string): number {
    const channel = (c: number) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);

    const luminance = (hex: string) => {
      const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.substr(i, 2), 16) / 255);

      return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
    };

    const [lighter, darker] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);

    return (lighter + 0.05) / (darker + 0.05);
  }

  /** Every surface a component can sit on. */
  const SURFACES = ['--bg-page', '--bg-surface', '--bg-raised'];

  /** Every token a component writes text in. */
  const TEXT = ['--text', '--text-muted', '--accent', '--positive', '--warning', '--danger'];

  /** Text on a fill or a tint rather than on a surface. */
  const ON_FILL: readonly (readonly [string, string])[] = [
    ['--on-accent', '--accent'],
    ['--on-accent', '--accent-hover'],
    ['--accent', '--accent-tint'],
  ];

  function failures(bar: number, pairs: readonly (readonly [string, string, string])[]) {
    return pairs
      .map(([theme, foreground, background]) => {
        const ratio = contrast(resolve(themes[theme], foreground), resolve(themes[theme], background));

        return { theme, foreground, background, ratio };
      })
      .filter(({ ratio }) => ratio < bar)
      .map((f) => `${f.theme}: ${f.foreground} on ${f.background} = ${f.ratio.toFixed(2)}:1`);
  }

  const themeNames = Object.keys(themes);

  it.each(themeNames)('reads as body text in the %s theme', (theme) => {
    const pairs = [
      ...TEXT.flatMap((fg) => SURFACES.map((bg) => [theme, fg, bg] as const)),
      ...ON_FILL.map(([fg, bg]) => [theme, fg, bg] as const),
    ];

    expect(failures(TEXT_BAR, pairs)).toEqual([]);
  });

  it.each(themeNames)('shows a focus ring against every surface in the %s theme', (theme) => {
    const pairs = SURFACES.map((bg) => [theme, '--focus-ring', bg] as const);

    expect(failures(INDICATOR_BAR, pairs)).toEqual([]);
  });

  /**
   * The brand primary is not the interface's accent, and something has to say so.
   *
   * ADR 0034 moved `--accent` to `--clay-600` and left `--clay-500` alone, and the
   * first version of that claim was wrong in a way no test could see: the
   * storefront's wordmark was set in `--accent`, so the brand's own name in the
   * header went darker with the button. Both wordmarks are set in `--brand` (an
   * alias of `--clay-500`) now, and they can be, because they are LARGE text
   * (20px at 800), which WCAG holds to 3:1 rather than 4.5:1.
   *
   * This pins the number that makes that legitimate, in both themes: the
   * storefront is light and the backoffice is dark, and `#D85A30` clears 3:1 on
   * every surface of each (3.43:1 on the light page, 3.88:1 on the dark raised
   * surface). If somebody lightens or darkens the brand colour, this is where it
   * turns red instead of a wordmark going illegible.
   */
  it.each(themeNames)('keeps the brand primary legible as large text in the %s theme', (theme) => {
    expect(resolve(themes[theme], '--brand')).toBe('#D85A30');
    expect(
      failures(
        LARGE_TEXT_BAR,
        SURFACES.map((surface) => [theme, '--brand', surface] as const),
      ),
    ).toEqual([]);
  });

  /**
   * And each wordmark has to actually be set in it, at a size that makes the
   * lower bar legitimate. The colour alone is not enough: at the backoffice's
   * 14px body size `#D85A30` is body text, held to 4.5:1, and it misses that on
   * the dark bar (4.33:1). The way the storefront's was lost was a one-word alias
   * (`--accent`) that looked equivalent and was not; the way the backoffice's
   * would be lost is a size nobody thought was load-bearing.
   */
  const WORDMARKS = [
    ['storefront', 'frontend/apps/storefront/src/app/layout/shell.css', 'brand__wordmark'],
    ['backoffice', 'frontend/apps/backoffice/src/app/layout/shell.css', 'bar__brand'],
  ] as const;

  it.each(WORDMARKS)(
    'sets the %s wordmark in the brand primary, as large text, never in the action alias',
    (_app, file, className) => {
      const shell = readFileSync(join(repository, file), 'utf8');
      const rule = shell.match(new RegExp(`\\.${className}\\s*\\{([^}]*)\\}`))?.[1] ?? '';

      expect(rule).not.toBe('');
      expect(rule).toContain('color: var(--brand)');
      expect(rule).not.toContain('var(--accent)');
      expect(rule).toContain('font-weight: 800');
      expect(rule).toContain('font-size: var(--text-lg)');
    },
  );

  /**
   * `--text-lg` is what makes both wordmarks large text, so its value is part of
   * the contrast argument, not only of the type scale.
   */
  it('keeps --text-lg at a size WCAG counts as large bold text', () => {
    const rem = root['--text-lg']?.match(/^([\d.]+)rem$/)?.[1];

    expect(rem).toBeDefined();
    expect(Number(rem) * ROOT_FONT_PX).toBeGreaterThanOrEqual(LARGE_BOLD_MIN_PX);
  });

  /**
   * The guard every rule in this repository carries: a scan that found nothing
   * reports no offenders and passes. Two of the three assertions below would
   * have caught the shape of this test's own first draft, which parsed the dark
   * block with a regex that matched nothing and therefore measured the light
   * palette twice.
   */
  it('is not passing over nothing', () => {
    expect(Object.keys(root).length).toBeGreaterThan(30);
    expect(Object.keys(dark).length).toBeGreaterThan(10);
    expect(resolve(themes.light, '--accent')).not.toEqual(resolve(themes.dark, '--accent'));

    // The measurement itself has to be right, or every pair above passes for
    // the wrong reason. Black on white is the one ratio nobody has to look up.
    expect(contrast('#000000', '#FFFFFF')).toBeCloseTo(21, 5);
    expect(contrast('#FFFFFF', '#FFFFFF')).toBeCloseTo(1, 5);
  });

  /**
   * `--text-subtle` was deleted rather than darkened (ADR 0034): the lightest
   * value that cleared the bar was 0.57 of ratio from `--text-muted`, which is
   * a distinction nobody can see and which only worked while it was below the
   * minimum. A token that comes back is a decision being reversed by accident.
   */
  it('has no third text tier, because AA leaves room for two', () => {
    expect(root['--text-subtle']).toBeUndefined();
    expect(dark['--text-subtle']).toBeUndefined();
  });
});
