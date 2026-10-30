import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * A mockup is a projection of the design system, never a second copy of it.
 *
 * `design-tokens.spec.ts` beside this file enforces the rule DESIGN.md states —
 * never write a hex outside `tokens.css` — and it keeps `.css` files, so an
 * artefact in any other format is outside it by construction. That gap is not
 * hypothetical: two design documents sat in `docs/design/` from 2026-09-03
 * carrying **575 hand-written hexadecimal colours**, describing a different
 * product, referenced by nothing, and no test could see them (ADR 0033).
 *
 * So this is the same rule, one format further out. It reads every `.pen` and
 * every `.html` under both halves of the rung — `design/mockups/`, which is
 * published, and `design/unpublished/mockups/`, which waits for its article —
 * documented JSON and plain text, so no application, no account and no container
 * are needed, and holds every colour they declare to the palette `tokens.css`
 * owns. An artefact does not become exempt by being early.
 *
 * **A new colour is allowed; a new colour smuggled in is not.** A mockup is
 * where the design system gets improved, which already happened once: the blind
 * design pass of 2026-09-18 found that `[data-theme='dark']` redefines
 * `--positive` and neither `--danger` nor `--warning`, and refused to invent a
 * hex rather than break the rule. That is the flow this test protects —
 * `.pen` proposes, a person approves, `tokens.css` changes — which is invariant
 * 12 applied to design instead of to the catalogue. A value that is not in
 * `tokens.css` must therefore be declared in `proposals.json` beside the artefact
 * with a reason, exactly as a `ProductClaim` carries its provenance.
 */
describe('the mockups', () => {
  const TOKENS = 'design/tokens.css';
  /**
   * Both halves of the ladder's second rung. `design/mockups/` is what has been
   * published; `design/unpublished/mockups/` is what waits for the article that
   * publishes it, and the publication filter removes it (rule 1 of *The public
   * repository*, measured on 2026-09-19: adding the path moved no hash, because
   * it had never existed). The rule holds on both sides, because an artefact does
   * not become exempt by being early.
   */
  const ROOTS = ['design'];
  const PROPOSALS = 'design/unpublished/mockups/proposals.json';

  /** `#fff`, `#ffffff`, `#ffffffff`. */
  const HEX = /#[0-9a-f]{3,8}\b/gi;

  /** Found by walking up to the file the rule is about, as its sibling does. */
  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, TOKENS))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${TOKENS} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  const palette = new Set(
    [...(readFileSync(join(repository, TOKENS), 'utf8').match(HEX) ?? [])].map((hex) =>
      hex.toLowerCase(),
    ),
  );

  /** Values a person has looked at and allowed, each with the reason it exists. */
  const proposed = (): Record<string, string> => {
    const path = join(repository, PROPOSALS);

    return existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : {};
  };

  function artefacts(extension: string): string[] {
    // Walked, not listed. The first version named the folders it knew about, and
    // the first sketch landed in `design/unpublished/sketches/` - outside the
    // scan, where a file full of hand-written hexes would have gone green. That
    // is the third time in one day that a rule held for one location while the
    // artefact was in another, so the rule now walks `design/` whole: a rung
    // added later is inside it the day the folder exists, not the day somebody
    // remembers.
    const walk = (directory: string): string[] =>
      existsSync(directory)
        ? readdirSync(directory).flatMap((entry) => {
            const path = join(directory, entry);

            return statSync(path).isDirectory()
              ? walk(path)
              : path.endsWith(extension)
                ? [path]
                : [];
          })
        : [];

    return ROOTS.flatMap((root) => walk(join(repository, root)));
  }

  const mockups = () => artefacts('.pen');

  /** Colour variables are the only place a `.pen` may name a colour. */
  function colours(path: string): string[] {
    const document = JSON.parse(readFileSync(path, 'utf8')) as {
      variables?: Record<string, { type?: string; value?: unknown }>;
    };

    return Object.values(document.variables ?? {})
      .filter((variable) => variable.type === 'color')
      .flatMap((variable) =>
        typeof variable.value === 'string'
          ? [variable.value]
          : // A themed value is an array of {value, theme} entries.
            (variable.value as { value?: string }[] | undefined)?.map((entry) => entry.value ?? '') ??
            [],
      )
      .filter(Boolean)
      .map((value) => value.toLowerCase());
  }

  it('name no colour the design system does not own', () => {
    const allowed = proposed();

    const offenders = mockups().flatMap((path) =>
      colours(path)
        .filter((hex) => !palette.has(hex) && !(hex in allowed))
        .map((hex) => `${relative(repository, path).replaceAll('\\', '/')}: ${hex}`),
    );

    expect(offenders).toEqual([]);
  });

  it('put every colour in a variable, so a fill can never be a literal', () => {
    // Properties only, never text. The first version of this rule matched a hex
    // anywhere in the stringified subtree, and the design-system sheet failed it
    // 21 times - every one of them a swatch printing the value it documents,
    // beside a fill that was a proper `$variable`:
    //
    //   {"ZFb9X": {"fill": "$clay-500"}, "qlchJ": {"content": "--clay-500"},
    //    "K3sL3V": {"content": "#D85A30"}}
    //
    // A sheet whose job is to document colours cannot be forbidden the `#`
    // character. So the walk skips `content` and flags a literal under any other
    // key, wherever it nests - a fill, a stroke, a gradient stop, or a property
    // override inside a component instance's `descendants` map.
    const literals = (value: unknown, key?: string): string[] => {
      if (typeof value === 'string') {
        // A fresh non-global matcher: `HEX` carries the `g` flag, and `.test()`
        // on a global regex advances `lastIndex`, so it would answer true and
        // false alternately for the same input.
        return key !== 'content' && /#[0-9a-f]{3,8}\b/i.test(value) ? [`${key}: ${value}`] : [];
      }

      if (Array.isArray(value)) return value.flatMap((entry) => literals(entry, key));

      if (value && typeof value === 'object') {
        return Object.entries(value).flatMap(([name, entry]) => literals(entry, name));
      }

      return [];
    };

    const offenders = mockups().flatMap((path) => {
      const document = JSON.parse(readFileSync(path, 'utf8')) as { children?: unknown };

      return literals(document.children ?? []).map(
        (hit) => `${relative(repository, path).replaceAll('\\', '/')}: literal ${hit}`,
      );
    });

    expect(offenders).toEqual([]);
  });

  it('export nothing the design system does not own either', () => {
    // The gap the blind pass pointed at, closed. A mockup is committed twice -
    // the `.pen` a reader inspects and the HTML a reader can open without
    // installing anything (ADR 0033) - and the export is where the variables
    // have already been flattened back into literals. Checking only the `.pen`
    // would leave the readable half unchecked, which is this repository's
    // recurring shape of defect: the rule holds for one format and the artefact
    // is in another.
    const allowed = proposed();

    const offenders = artefacts('.html').flatMap((path) =>
      [...new Set((readFileSync(path, 'utf8').match(HEX) ?? []).map((hex) => hex.toLowerCase()))]
        .filter((hex) => !palette.has(hex) && !(hex in allowed))
        .map((hex) => `${relative(repository, path).replaceAll('\\', '/')}: ${hex}`),
    );

    expect(offenders).toEqual([]);
  });

  it('draw nothing in Excalidraw the design system does not own', () => {
    // The same rule, one canvas further out. The sketches were redrawn in
    // Excalidraw on 2026-10-02 to compare it with pen.dev, and Excalidraw has no
    // variables: every stroke and fill is a literal hex in the file. So the file
    // is held the way an export is - every hex in it, wherever it sits, must be
    // the palette's. Without this test the `.excalidraw` files would have been
    // the 575 colours of `docs/design/` again: JSON beside the rule, never read.
    const allowed = proposed();
    const sketches = artefacts('.excalidraw');

    const offenders = sketches.flatMap((path) =>
      [...new Set((readFileSync(path, 'utf8').match(HEX) ?? []).map((hex) => hex.toLowerCase()))]
        .filter((hex) => !palette.has(hex) && !(hex in allowed))
        .map((hex) => `${relative(repository, path).replaceAll('\\', '/')}: ${hex}`),
    );

    expect(offenders).toEqual([]);

    // And the parser reached inside each one: a drawing with elements and no
    // colour at all would mean the format changed under us, not a clean file.
    for (const path of sketches) {
      const { elements } = JSON.parse(readFileSync(path, 'utf8')) as {
        elements?: { strokeColor?: string }[];
      };
      if (elements && elements.length > 0) {
        expect(elements.some((element) => /^#[0-9a-f]{3,8}$/i.test(element.strokeColor ?? ''))).toBe(
          true,
        );
      }
    }
  });

  it('are actually there, so the rule is not passing over nothing', () => {
    // The same guard every architecture rule in this repository carries: a scan
    // that walks a directory which is not there finds no offenders and reports
    // success. What it asserts is that the scan FOUND its ground, not that the
    // ground had files on it - a public clone taken before the first mockup is
    // promoted has an empty `design/mockups/` and must still go green.
    expect(palette.size).toBeGreaterThan(20);
    expect(ROOTS.some((root) => existsSync(join(repository, root)))).toBe(true);

    // And where a document has been drawn into, the parser reached inside it: a
    // `.pen` with nodes but no colour at all would mean the format changed under
    // us. An empty document is exempt, because a file created and not yet drawn
    // into is an ordinary state and not a broken parser - pen.dev writes the stub
    // first and the content when somebody saves.
    for (const path of mockups()) {
      const drawn = (JSON.parse(readFileSync(path, 'utf8')) as { children?: unknown[] }).children;
      if (drawn && drawn.length > 0) expect(colours(path).length).toBeGreaterThan(0);
    }
  });

  it('declare a reason for any colour the design system does not own yet', () => {
    // A proposal is a claim: it carries why it exists, or it is not a proposal.
    const withoutReason = Object.entries(proposed())
      .filter(([, reason]) => typeof reason !== 'string' || reason.trim().length < 20)
      .map(([hex]) => hex);

    expect(withoutReason).toEqual([]);
  });
});
