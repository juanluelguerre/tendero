import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * A size in a template names a token, as a colour does (ADR 0050).
 *
 * The migration to utilities (ADR 0049) carried 275 literal lengths across from
 * the old stylesheets — `min-h-[2.75rem]` seventy times, the 44px a finger needs
 * — so the theme's promise, "a utility can only name a token", held for colours
 * and not for sizes. They are tokens now (`min-h-tap`), and this keeps them so:
 * a lone length in brackets is a size the design has not named.
 *
 * What it does not forbid, on purpose: a layout written as an expression —
 * `grid-cols-[minmax(0,1fr)_320px]`, `w-[min(28rem,calc(100vw-…))]` — which is
 * what Tailwind's brackets are for and what a global token would only move away
 * from its markup; and a reference to a token, `duration-(--dur-fast)`.
 */
describe('the templates', () => {
  const MARKER = 'design/theme.css';

  const SKIP = new Set(['node_modules', 'dist', '.angular', '.nx', 'coverage']);

  /** A utility, with any variants, whose bracket holds one bare length and nothing else. */
  const LONE_LENGTH = /[\w:/[\]=.&-]*-\[-?[\d.]+(?:px|rem|em|ch|%|vh|vw|dvh|svh|lvh)\]/g;

  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, MARKER))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${MARKER} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  function templates(directory: string): string[] {
    return readdirSync(directory).flatMap((entry) => {
      const path = join(directory, entry);

      if (SKIP.has(entry)) return [];
      if (statSync(path).isDirectory()) return templates(path);

      return path.endsWith('.html') ? [path] : [];
    });
  }

  const files = ['frontend/apps', 'frontend/libs']
    .flatMap((root) => templates(join(repository, root)))
    .map((path) => [relative(repository, path).replaceAll('\\', '/'), readFileSync(path, 'utf8')] as const);

  it('size things with tokens, never with a length written in brackets', () => {
    const offenders = files.flatMap(([name, html]) =>
      [...html.matchAll(LONE_LENGTH)].map(([utility]) => `${name}: ${utility}`),
    );

    expect(offenders).toEqual([]);
  });

  it('are not passing over nothing', () => {
    expect(files.length).toBeGreaterThan(100);
    expect(files.some(([, html]) => html.includes('min-h-tap'))).toBe(true);
  });
});
