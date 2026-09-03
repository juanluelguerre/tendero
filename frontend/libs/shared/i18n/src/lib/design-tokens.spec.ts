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
  /** The one file allowed to contain a raw colour. */
  const SOURCE = 'design/tokens.css';

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
      .filter(([name]) => name !== SOURCE)
      .flatMap(([name, path]) => {
        // Comments are prose and may name a colour; the rule is about values.
        const css = readFileSync(path, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

        return [...(css.match(HEX) ?? [])].map((hex) => `${name}: ${hex}`);
      });

    expect(offenders).toEqual([]);
  });

  it('are actually defined, so the rule is not passing over nothing', () => {
    const tokens = readFileSync(join(repository, SOURCE), 'utf8');

    expect(tokens.match(HEX)?.length ?? 0).toBeGreaterThan(20);
    expect(stylesheets(repository).length).toBeGreaterThan(10);
  });
});
