import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * The gate that was missing when a stylesheet was edited by a script.
 *
 * On 2026-09-20 the accessibility pass (ADR 0034) migrated 51 declarations
 * across 22 stylesheets with a script, and the script corrupted two of them:
 *
 *     .lang--active { background: var(--accencolor: var(--color-on-accent)e-0); }
 *     color: var(--color-on-accent)0);
 *
 * It had collected its matches against the original text and then spliced into
 * a string it was mutating, so every replacement after the first in a file
 * landed two characters off. **Nothing went red.** `dotnet build` does not read
 * CSS, eslint does not parse it, vitest had no opinion, and
 * `design-tokens.spec.ts` looks for hex literals — of which a mangled `var()`
 * has none. A browser drops a malformed declaration in silence, so the language
 * switch simply lost its background and the cart badge its colour, on a page
 * that still rendered and still passed every contrast check, because the text
 * fell back to an inherited colour that happened to be legible.
 *
 * The rule below is what would have caught it, and it is sharper than "the CSS
 * parses" because it encodes why a missing token is fatal:
 *
 * **A `var()` with no fallback must name a custom property that exists.** With
 * a fallback — `var(--accent-soft, var(--color-raised))` — an undefined property
 * is a deliberate, working degradation and the declaration stands. Without one,
 * the whole declaration is invalid at computed-value time and the colour falls
 * through to whatever is inherited. That is how `--color-on-accent` was referenced by
 * a button and defined nowhere, giving 2.45:1 on an accent fill with nothing
 * red anywhere (ADR 0034).
 *
 * **What this file does not cover, measured rather than assumed.** Against the
 * three mutations that matter, run one spec at a time:
 *
 *     .lang--active corrupted        integrity RED    contrast green
 *     cart badge corrupted           integrity RED    contrast green
 *     --color-on-accent out of :root       integrity green  contrast RED
 *
 * The last one survives here because `defined` is theme-blind: the dark block
 * still declares `--color-on-accent`, so the name exists somewhere and this file is
 * satisfied. `design-contrast.spec.ts` resolves per theme and throws on it. The
 * two are complementary and neither is enough alone, which is why the gap is
 * written down instead of being papered over with a second set of rules here.
 */
describe('the stylesheets', () => {
  const TOKENS = 'design/tokens.css';

  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, TOKENS))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${TOKENS} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  /**
   * The repository's stylesheets as git sees them, as `design-tokens.spec.ts`
   * reads them: tracked, or new and not ignored. A third-party theme in an
   * ignored folder is not the repository's, and failed here on one machine only.
   */
  function stylesheets(directory: string): string[] {
    return execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z', '--', '*.css'], {
      cwd: directory,
      encoding: 'utf8',
    })
      .split('\0')
      .filter(Boolean)
      .map((path) => join(directory, path))
      .filter((path) => existsSync(path));
  }

  const files = stylesheets(repository).map(
    (path) => [relative(repository, path).replaceAll('\\', '/'), readFileSync(path, 'utf8')] as const,
  );

  /** Comments may discuss a token by name without defining or using it. */
  const code = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');

  /**
   * Everything the repository defines anywhere: `design/tokens.css`, the
   * Tailwind bridge's `@theme` block, and the handful a component declares for
   * itself. A definition is a custom property followed by a colon.
   */
  const defined = new Set(files.flatMap(([, css]) => [...code(css).matchAll(/(--[\w-]+)\s*:/g)].map((m) => m[1])));

  it('only read custom properties that exist, unless they name a fallback', () => {
    const offenders = files.flatMap(([name, css]) =>
      [...code(css).matchAll(/var\(\s*(--[\w-]+)\s*(.?)/g)]
        // A comma means a fallback follows, and the declaration survives.
        // Anything else — including the ':' a corrupted splice leaves behind —
        // means this reference is the whole of it.
        .filter(([, property, next]) => next !== ',' && !defined.has(property))
        .map(([, property]) => `${name}: var(${property}) is read but never defined`),
    );

    expect(offenders).toEqual([]);
  });

  it('open every var() on a property reference, not on debris', () => {
    const offenders = files.flatMap(([name, css]) =>
      [...code(css).matchAll(/var\(/g)]
        .filter((m) => !/^\s*--[\w-]+\s*[,)]/.test(code(css).slice(m.index + 4)))
        .map(
          (m) =>
            `${name}: ${
              code(css)
                .slice(m.index, m.index + 60)
                .split('\n')[0]
            }`,
        ),
    );

    expect(offenders).toEqual([]);
  });

  it('keep their parentheses balanced, so no declaration is silently dropped', () => {
    const offenders = files.flatMap(([name, css]) =>
      code(css)
        .split(/[;{}]/)
        .filter((declaration) => declaration.includes('var('))
        .filter((declaration) => (declaration.match(/\(/g) ?? []).length !== (declaration.match(/\)/g) ?? []).length)
        .map((declaration) => `${name}: ${declaration.trim().slice(0, 80)}`),
    );

    expect(offenders).toEqual([]);
  });

  /**
   * The guard every rule here carries: a walk that found nothing reports no
   * offenders and passes. `--color-accent`, `--color-body` and `--color-page` are the three
   * this system cannot lose without everything else being wrong too.
   */
  it('are not passing over nothing', () => {
    expect(files.length).toBeGreaterThan(10);
    expect(defined.size).toBeGreaterThan(50);

    for (const property of ['--color-accent', '--color-body', '--color-page', '--color-on-accent']) {
      expect(defined).toContain(property);
    }
  });
});
