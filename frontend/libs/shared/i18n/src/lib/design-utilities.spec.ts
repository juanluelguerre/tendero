import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, relative, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Every class a template writes either makes CSS or is ours (ADR 0050).
 *
 * Tailwind ignores a class it cannot build, in silence: no warning, no failed
 * build, a page that simply loses a rule. When the tokens became the theme and
 * the bridge went, `font-bold`, `p-0` and `bg-transparent` stopped existing —
 * the bridge had declared `--font-weight-*`, `--spacing-0` and
 * `--color-transparent`, the new source did not — and nothing went red. The
 * Prettier plugin noticed first, by sorting `font-bold` to the front, which is
 * where it puts what it does not know.
 *
 * So this asks Tailwind itself, through the design system its own editor
 * tooling and Prettier plugin load, whether each class builds; a class that
 * does not must be one of ours — defined in a stylesheet, or a BEM hook a test
 * or a stylesheet finds (`card__link`, `tag--danger`). The API is marked
 * unstable: if an upgrade changes it, this fails loudly, which is the point.
 */
describe('the templates', () => {
  const MARKER = 'design/theme.css';
  const SKIP = new Set(['node_modules', 'dist', '.angular', '.nx', 'coverage']);

  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, MARKER))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${MARKER} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  const frontend = join(repository, 'frontend');
  const requireFromFrontend = createRequire(join(frontend, 'package.json'));
  // Required rather than imported: Vite resolves the package's `style` entry (its CSS).
  const { __unstable__loadDesignSystem } = requireFromFrontend('tailwindcss') as typeof import('tailwindcss');

  function walk(directory: string, extension: string): string[] {
    return readdirSync(directory).flatMap((entry) => {
      const path = join(directory, entry);

      if (SKIP.has(entry)) return [];
      if (statSync(path).isDirectory()) return walk(path, extension);

      return path.endsWith(extension) && !path.endsWith(`.spec${extension}`) ? [path] : [];
    });
  }

  /** Every file with this ending, specs included (the end-to-end suites are all specs). */
  function walkAll(directory: string, ending: string): (readonly [string, string])[] {
    return readdirSync(directory).flatMap((entry) => {
      const path = join(directory, entry);

      if (SKIP.has(entry)) return [];
      if (statSync(path).isDirectory()) return walkAll(path, ending);

      return path.endsWith(ending)
        ? [[relative(repository, path).replaceAll('\\', '/'), readFileSync(path, 'utf8')] as const]
        : [];
    });
  }

  const roots = ['apps', 'libs'].map((root) => join(frontend, root));
  const read = (extension: string) =>
    roots
      .flatMap((root) => walk(root, extension))
      .map((path) => [relative(repository, path).replaceAll('\\', '/'), readFileSync(path, 'utf8')] as const);

  /** Static classes, `[class.x]` bindings, the strings of a `[class]` expression, and host classes. */
  function classesIn(source: string): string[] {
    const found = [
      ...[...source.matchAll(/\sclass="([^"]*)"/g)].flatMap(([, list]) => list.split(/\s+/)),
      ...[...source.matchAll(/\[class\.([^\]=\s]+)\]/g)].map(([, name]) => name),
      ...[...source.matchAll(/\[class\]="([^"]*)"/g)].flatMap(([, expression]) =>
        // The strings a ternary chooses between, not the ones it compares with (`size === 'S'`).
        [...expression.matchAll(/'([^']*)'/g)]
          .filter((match) => !/[=!]==?\s*$/.test(expression.slice(0, match.index)))
          .flatMap(([, list]) => list.split(/\s+/)),
      ),
      ...[...source.matchAll(/host:\s*\{([^}]*)\}/g)].flatMap(([, host]) =>
        [...host.matchAll(/(?:^|[\s,{])class:\s*'([^']*)'/g)].flatMap(([, list]) => list.split(/\s+/)),
      ),
    ];

    return found.filter((name) => name && !name.includes('{{'));
  }

  const used = new Map<string, string>();
  for (const [name, source] of [...read('.html'), ...read('.ts')]) {
    for (const utility of classesIn(source)) if (!used.has(utility)) used.set(utility, name);
  }

  /** What our own stylesheets define: the apps' primitives, the tokens' element rules, components' classes. */
  const ours = new Set(
    [...read('.css'), ['design/theme.css', readFileSync(join(repository, MARKER), 'utf8')] as const].flatMap(
      ([, css]) => [...css.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/\.(-?[A-Za-z_][\w-]*)/g)].map(([, name]) => name),
    ),
  );

  /**
   * What a test or the code finds by class — `.wall-head`, `.chip` — in a selector
   * inside a string, including the end-to-end suites'. A hook makes no CSS and
   * needs none; ADR 0049 asks that a class kept in a template be one.
   */
  const selected = new Set(
    [...read('.ts'), ...['apps'].flatMap(() => walkAll(join(frontend, 'apps'), '.spec.ts'))]
      .flatMap(([, ts]) => [...ts.matchAll(/(['"`])((?:(?!\1)[^\n])*)\1/g)])
      .flatMap(([, , text]) => [...text.matchAll(/\.(-?[A-Za-z_][\w-]*)/g)].map(([, name]) => name)),
  );

  /** A BEM element or modifier is a hook too, for a stylesheet or a test. */
  const hook = (name: string) => selected.has(name) || name.includes('__') || /[a-z0-9]--[A-Za-z]/.test(name);

  /** Tailwind's markers: `group` and `peer`, named or not, make no CSS of their own. */
  const marker = (name: string) => /^(group|peer)(\/[\w-]+)?$/.test(name);

  it('build every utility they name', async () => {
    const entry = join(frontend, 'libs', 'shared', 'tokens', 'src', 'theme.css');
    const system = await __unstable__loadDesignSystem(readFileSync(entry, 'utf8'), {
      base: dirname(entry),
      loadStylesheet: async (id: string, base: string) => {
        const path = id.startsWith('.') ? resolve(base, id) : requireFromFrontend.resolve(id);
        return { path, base: dirname(path), content: readFileSync(path, 'utf8') };
      },
    });

    const candidates = [...used.keys()].filter((name) => !ours.has(name) && !hook(name) && !marker(name));
    const built = system.candidatesToCss(candidates);
    const offenders = candidates
      .filter((_, index) => built[index] === null)
      .map((name) => `${used.get(name)}: ${name}`);

    expect(offenders).toEqual([]);
  });

  it('are not passing over nothing', () => {
    expect(used.size).toBeGreaterThan(300);
    expect(used.has('font-bold')).toBe(true);
  });
});
