import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * A component's own element is styled in its metadata, `host: { class: 'block' }`,
 * and its stylesheet exists only for what utilities cannot say (ADR 0049).
 *
 * Both halves can fail in silence. Tailwind reads templates and never TS, so a
 * host class is generated only because `theme.css` names it in its
 * `@source inline(…)`; one missing there is a host with no style and no error —
 * a `contents` pane turning into a box in the middle of the list-and-pane grid.
 * And a stylesheet with nothing in it is a file every reader opens for nothing,
 * which is why the 93 there were on 2026-10-01 went.
 */
describe('the components', () => {
  const THEME = 'frontend/libs/shared/tokens/src/theme.css';

  const SKIP = new Set(['node_modules', 'dist', '.angular', '.nx', 'coverage', 'generated']);

  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, THEME))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${THEME} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  function walk(directory: string, extension: string): string[] {
    return readdirSync(directory).flatMap((entry) => {
      const path = join(directory, entry);

      if (SKIP.has(entry)) return [];
      if (statSync(path).isDirectory()) return walk(path, extension);

      return path.endsWith(extension) && !path.endsWith(`.spec${extension}`) ? [path] : [];
    });
  }

  const roots = ['frontend/apps', 'frontend/libs'].map((root) => join(repository, root));
  const read = (extension: string) =>
    roots
      .flatMap((root) => walk(root, extension))
      .map((path) => [relative(repository, path).replaceAll('\\', '/'), readFileSync(path, 'utf8')] as const);

  const components = read('.ts').filter(([, ts]) => ts.includes('@Component('));

  /** The value of `class:` inside a `host: { … }` object, however it is wrapped. */
  const hostClasses = components.flatMap(([name, ts]) =>
    [...ts.matchAll(/host:\s*\{([^}]*)\}/g)].flatMap(([, host]) =>
      [...host.matchAll(/(?:^|[\s,{])class:\s*'([^']*)'/g)].flatMap(([, value]) =>
        value
          .split(/\s+/)
          .filter(Boolean)
          .map((utility) => [name, utility] as const),
      ),
    ),
  );

  const theme = readFileSync(join(repository, THEME), 'utf8');
  const safelisted = new Set(
    [...theme.matchAll(/@source inline\(\s*'([^']*)'\s*\)/g)].flatMap(([, list]) => list.split(/\s+/)),
  );

  it('style their host only with utilities the theme generates', () => {
    const missing = hostClasses
      .filter(([, utility]) => !safelisted.has(utility))
      .map(([name, utility]) => `${name}: '${utility}' is not in theme.css's @source inline(…)`);

    expect(missing).toEqual([]);
  });

  it('have a stylesheet only when it says something', () => {
    const empty = read('.css')
      .filter(([name]) => /\/src\/app\/|\/src\/lib\//.test(name))
      .filter(([, css]) => css.replace(/\/\*[\s\S]*?\*\//g, '').trim() === '')
      .map(([name]) => name);

    expect(empty).toEqual([]);
  });

  /** A walk that found nothing passes; this is the guard against it. */
  it('are not passing over nothing', () => {
    expect(components.length).toBeGreaterThan(100);
    expect(hostClasses.length).toBeGreaterThan(60);
    expect(safelisted.has('contents')).toBe(true);
  });
});
