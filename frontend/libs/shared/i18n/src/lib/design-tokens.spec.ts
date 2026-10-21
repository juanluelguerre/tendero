import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * The design system's one hard rule, made executable.
 *
 * `design/DESIGN.md` says it plainly: **never write a hex outside
 * `tokens.css`**. If a component needs a colour that does not exist, that is a
 * design decision and not a CSS fix.
 *
 * Until now it was verified by reading — `docs/analysis/current-state.md`
 * records "no hex literal appears in any component file" as something somebody
 * checked by hand on 2026-09-02. That is the shape of promise this repository
 * keeps turning into a gate, and what had been in the way is that half the
 * styles lived inside TypeScript template literals, where no CSS tool can see
 * them. They are `.css` files now, so this is twenty lines and no dependency.
 *
 * It scans the **whole repository**, not just `frontend/`, because DESIGN.md's
 * rule is about four surfaces and only two of them are Angular apps.
 *
 * It lives in `shared-i18n` for an ordinary reason: it is the library that
 * already has vitest configured, and a project whose only content is one test
 * would be more ceremony than test.
 */
describe('the design tokens', () => {
  /** The one file allowed to contain a raw colour (ADR 0050)… */
  const SOURCE = 'design/theme.css';
  /** …and its copy for pages with no Tailwind, which tools/tokens.mjs writes from it… */
  const GENERATED = 'design/tokens.css';
  /**
   * …and the same copy inside the sign-in page's theme (keycloak/themes/tendero),
   * which Keycloak serves from the theme alone. Exempt only while it is the copy
   * byte for byte: the test below holds it to that.
   */
  const SIGN_IN_COPY = 'keycloak/themes/tendero/login/resources/css/tokens.css';

  /** `#fff`, `#ffffff`, `#ffffffff`. */
  const HEX = /#[0-9a-f]{3,8}\b/gi;

  const SKIP = new Set(['node_modules', 'dist', '.angular', '.git', 'bin', 'obj', 'coverage']);

  /**
   * Found by walking up to the file the rule is about, rather than by counting
   * `..` segments.
   *
   * Counting was wrong by one on the first attempt, and the scan PASSED anyway:
   * it walked a directory that does not exist, found nothing, and reported no
   * offenders. That vacuous pass is what the second test below exists to catch,
   * and it caught it — the same guard every architecture rule in this repository
   * carries.
   */
  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, SOURCE))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${SOURCE} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  function stylesheets(directory: string): string[] {
    return readdirSync(directory).flatMap((entry) => {
      const path = join(directory, entry);

      if (SKIP.has(entry)) return [];
      if (statSync(path).isDirectory()) return stylesheets(path);

      return path.endsWith('.css') ? [path] : [];
    });
  }

  it('are the only place a colour is written down', () => {
    const offenders = stylesheets(repository)
      .map((path) => [relative(repository, path).replaceAll('\\', '/'), path] as const)
      .filter(([name]) => name !== SOURCE && name !== GENERATED && name !== SIGN_IN_COPY)
      .flatMap(([name, path]) => {
        // Comments are prose and may name a colour; the rule is about values.
        const css = readFileSync(path, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

        return [...(css.match(HEX) ?? [])].map((hex) => `${name}: ${hex}`);
      });

    expect(offenders).toEqual([]);
  });

  /** The sign-in page's copy is exempt above because it is the generated file, not a second palette. */
  it('copy into the sign-in page’s theme byte for byte', () => {
    expect(readFileSync(join(repository, SIGN_IN_COPY), 'utf8')).toBe(
      readFileSync(join(repository, GENERATED), 'utf8'),
    );
  });

  /**
   * A component names what a colour is for, never which paint it is.
   *
   * `--color-red-50` is a pale pink in every theme, because no theme redefines a
   * palette step — only the semantic tokens change. A failed search's box used
   * it, and under the dark theme's light text the title vanished (the author,
   * the redesign's second walkthrough); a selected variant and the skeleton's
   * sweep did the same with the stone steps. So an app's stylesheet reaches the
   * palette only where a colour is meant to stay the same in both themes, and
   * each such place says why.
   */
  const PALETTE = /var\(--(stone|ink|clay|olive|amber|red|green)-\d+\)/g;

  /** Where a palette step is the point, not an accident. */
  const THEMELESS: Record<string, string> = {
    'frontend/libs/shared/tokens/src/theme.css': 'the bridge that maps the palette into Tailwind',
    'frontend/apps/backoffice/src/styles.css': "the light theme's state colours, defined from the palette",
    'frontend/apps/backoffice/src/app/shell/nav/nav.css':
      'the awning stripes of the current page: brand, the same in both themes',
    'frontend/apps/backoffice/src/app/shell/palette/palette.css': 'a scrim: ink over the page, dark in both themes',
    'frontend/apps/backoffice/src/app/shell/shell.css': 'a scrim: ink over the page, dark in both themes',
    'frontend/apps/backoffice/src/app/worklist/confirm-dialog/confirm-dialog.css':
      'a scrim: ink over the page, dark in both themes',
    'frontend/apps/storefront/src/app/shell/shell.css': 'a scrim: ink over the page, dark in both themes',
    'frontend/apps/storefront/src/app/listing/listing.css': 'a scrim: ink over the page, dark in both themes',
  };

  it('are reached by purpose in the apps, so a theme can change them', () => {
    const offenders = stylesheets(join(repository, 'frontend'))
      .map((path) => [relative(repository, path).replaceAll('\\', '/'), path] as const)
      .filter(([name]) => !(name in THEMELESS))
      .flatMap(([name, path]) => {
        const css = readFileSync(path, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
        return [...(css.match(PALETTE) ?? [])].map((step) => `${name}: ${step}`);
      });

    expect(offenders).toEqual([]);
  });

  it('are actually defined, so the rule is not passing over nothing', () => {
    const tokens = readFileSync(join(repository, SOURCE), 'utf8');

    expect(tokens.match(HEX)?.length ?? 0).toBeGreaterThan(20);
    expect(stylesheets(repository).length).toBeGreaterThan(10);
  });
});
