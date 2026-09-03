import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Each app wears its own face, and the file it names exists.
 *
 * This has already gone wrong once. The comment sitting in both `index.html`
 * files records it: *"the one Nx scaffolded was still sitting here, identical in
 * both apps, so a browser that preferred it showed the framework's logo on the
 * shop's tab."* Two apps, one copy-pasted head, and the only symptom is a
 * 16-pixel image nobody looks at while they are working.
 *
 * A favicon has no compiler and no test until somebody writes one. A renamed
 * asset, a path typed with the wrong app's name, or a head copied from the
 * sibling all fail exactly the same way — silently, and only in a browser tab.
 *
 * **What this does NOT cover, said out loud:** the browser. Chrome keeps
 * favicons in a database of their own, keyed by page URL, and it survives a hard
 * reload — so a stale icon in a tab can be correct on disk, correct on the wire
 * and wrong on screen. That is a cache, not a defect, and no test can see it.
 * What this covers is everything up to the wire.
 *
 * It lives beside the design-token scan because it is the same shape: a
 * repository-wide asset invariant that no unit under test owns.
 */
describe('the app icons', () => {
  const MARKER = 'design/tokens.css';

  /** Walking up to a known file rather than counting `..`, for the reason the
   *  token scan learned the hard way: a miscount scans nothing and passes. */
  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, MARKER))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${MARKER} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  const apps = ['storefront', 'backoffice'] as const;

  /** Every `rel="icon"` href an app's index.html declares, base-relative. */
  function declaredIcons(app: string): string[] {
    const html = readFileSync(
      join(repository, 'frontend', 'apps', app, 'src', 'index.html'),
      'utf8',
    );

    return [...html.matchAll(/<link[^>]*rel="[^"]*icon[^"]*"[^>]*>/gi)]
      .map((tag) => /href="([^"]+)"/i.exec(tag[0])?.[1])
      .filter((href): href is string => Boolean(href));
  }

  it.each(apps)('%s points at icons that exist', (app) => {
    const icons = declaredIcons(app);

    // Two: the SVG every modern browser prefers, and the .ico fallback. A head
    // that declared neither would pass a "no broken link" check trivially.
    expect(icons).toHaveLength(2);

    for (const href of icons) {
      // `<base href="/">` makes these resolve against the app root, which is
      // what `public/` is served as.
      const onDisk = join(repository, 'frontend', 'apps', app, 'public', href);
      expect(existsSync(onDisk), `${app}: index.html names ${href}, which does not exist`).toBe(
        true,
      );
    }
  });

  /**
   * The failure that actually happened: one app's head copied into the other.
   * Nothing about a build, a lint or a test notices two apps sharing a face.
   */
  it('gives the two apps different icons', () => {
    const [storefront, backoffice] = apps.map(declaredIcons);

    expect(storefront).not.toEqual(backoffice);

    for (const [app, icons] of apps.map((app, i) => [app, [storefront, backoffice][i]] as const)) {
      const svg = icons.find((href) => href.endsWith('.svg'));
      expect(svg, `${app} declares no SVG icon`).toBeDefined();

      // The surface it belongs to, in the filename. `-shop` and `-backoffice`
      // are what brand/BRAND.md calls the per-surface variants.
      const surface = app === 'storefront' ? 'shop' : 'backoffice';
      expect(svg).toContain(surface);
    }
  });

  /**
   * And the `.ico` files are not one file in two places.
   *
   * They are generated from the two SVGs, so identical bytes mean one of them
   * was generated from the wrong source — which is the exact state the repo was
   * in when both apps still carried the framework's scaffolded icon.
   */
  it('gives the two apps different .ico files', () => {
    const [storefront, backoffice] = apps.map((app) =>
      readFileSync(join(repository, 'frontend', 'apps', app, 'public', 'favicon.ico')),
    );

    expect(storefront.equals(backoffice)).toBe(false);
  });
});
