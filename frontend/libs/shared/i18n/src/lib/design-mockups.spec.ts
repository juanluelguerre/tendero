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
 * So this is the same rule, one format further out. It reads the `.pen`
 * documents under `design/mockups/` — documented JSON, so no application, no
 * account and no container are needed — and holds every colour they declare to
 * the palette `tokens.css` owns.
 *
 * **A new colour is allowed; a new colour smuggled in is not.** A mockup is
 * where the design system gets improved, which already happened once: the blind
 * design pass of 2026-09-18 found that `[data-theme='dark']` redefines
 * `--positive` and neither `--danger` nor `--warning`, and refused to invent a
 * hex rather than break the rule. That is the flow this test protects —
 * `.pen` proposes, a person approves, `tokens.css` changes — which is invariant
 * 12 applied to design instead of to the catalogue. A value that is not in
 * `tokens.css` must therefore be declared in `design/mockups/proposals.json`
 * with a reason, exactly as a `ProductClaim` carries its provenance.
 */
describe('the mockups', () => {
  const TOKENS = 'design/tokens.css';
  const MOCKUPS = 'design/mockups';
  const PROPOSALS = 'design/mockups/proposals.json';

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

  function mockups(): string[] {
    const directory = join(repository, MOCKUPS);
    if (!existsSync(directory)) return [];

    return readdirSync(directory)
      .map((entry) => join(directory, entry))
      .filter((path) => !statSync(path).isDirectory() && path.endsWith('.pen'));
  }

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
    const directory = join(repository, MOCKUPS);
    const exports = existsSync(directory)
      ? readdirSync(directory)
          .map((entry) => join(directory, entry))
          .filter((path) => !statSync(path).isDirectory() && path.endsWith('.html'))
      : [];

    const offenders = exports.flatMap((path) =>
      [...new Set((readFileSync(path, 'utf8').match(HEX) ?? []).map((hex) => hex.toLowerCase()))]
        .filter((hex) => !palette.has(hex) && !(hex in allowed))
        .map((hex) => `${relative(repository, path).replaceAll('\\', '/')}: ${hex}`),
    );

    expect(offenders).toEqual([]);
  });

  it('are actually there, so the rule is not passing over nothing', () => {
    // The same guard every architecture rule in this repository carries: a scan
    // that walks an empty directory finds no offenders and reports success.
    expect(palette.size).toBeGreaterThan(20);
    expect(mockups().length).toBeGreaterThan(0);
    expect(mockups().flatMap(colours).length).toBeGreaterThan(0);
  });

  it('declare a reason for any colour the design system does not own yet', () => {
    // A proposal is a claim: it carries why it exists, or it is not a proposal.
    const withoutReason = Object.entries(proposed())
      .filter(([, reason]) => typeof reason !== 'string' || reason.trim().length < 20)
      .map(([hex]) => hex);

    expect(withoutReason).toEqual([]);
  });
});
