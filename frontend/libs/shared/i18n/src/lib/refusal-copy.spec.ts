import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Every reason the shop can refuse an order has words, in both languages, in
 * every app that shows it.
 *
 * **This test exists because the bug it guards had no symptom.** An order the
 * stock saga cancelled used to say `Cancelled` and nothing else: the reason
 * travelled inside `OrderCancelled`, the outbox marked the message processed,
 * and it was gone. Nothing was red. The shopper saw a cancelled order with no
 * explanation and the shopkeeper had no row to point at.
 *
 * Persisting the reason fixed half of it. The other half is that the reason is
 * a CODE, and a code with no translation renders as the key — which looks like
 * a bug in a way that is somehow worse than saying nothing at all. Adding a
 * fourth reason in C# and forgetting four JSON files is one commit away at all
 * times, and only a browser in the right language would ever show it.
 *
 * The codes are read from `Order.cs` rather than listed here, for the reason
 * `Solution.cs` gives about assemblies: a list typed twice is a list that goes
 * out of date silently. Delete a constant and this test stops looking for it;
 * add one and it demands the words on the same commit.
 */
describe('the refusal copy', () => {
  const MARKER = 'design/tokens.css';

  const repository = (() => {
    let directory = __dirname;

    while (!existsSync(join(directory, MARKER))) {
      const parent = dirname(directory);
      if (parent === directory) throw new Error(`${MARKER} not found above ${__dirname}`);
      directory = parent;
    }

    return directory;
  })();

  /** The `public const string` members of `OrderStop`, straight from the domain. */
  const codes = (() => {
    const source = readFileSync(join(repository, 'src/Ordering/Domain/Order.cs'), 'utf8');
    const declaration = source.slice(source.indexOf('public sealed record OrderStop'));
    const body = declaration.slice(0, declaration.indexOf('\n}'));

    return [...body.matchAll(/public const string \w+ = "(?<code>[a-z_]+)";/g)]
      .map((match) => match.groups?.['code'])
      .filter((code): code is string => code !== undefined);
  })();

  /** Where each app keeps them: the section differs, the codes do not. */
  const surfaces = [
    { app: 'storefront', section: 'order' },
    { app: 'backoffice', section: 'orders' },
  ];

  const cultures = ['es', 'en'];

  it('finds the codes in the domain rather than trusting this file', () => {
    // A regex that matched nothing would let every assertion below pass in
    // vacuum — the same trap the architecture rules avoid by asserting their
    // computed dependency list came out non-empty.
    expect(codes.length).toBeGreaterThanOrEqual(3);
    expect(codes).toContain('out_of_stock');
  });

  it.each(
    surfaces.flatMap(({ app, section }) =>
      cultures.map((culture) => ({ app, section, culture })),
    ),
  )('says why in $app/$culture', ({ app, section, culture }) => {
    const path = join(repository, 'frontend/apps', app, 'public/i18n', `${culture}.json`);
    const stop = JSON.parse(readFileSync(path, 'utf8'))[section]?.['stop'] ?? {};

    for (const code of codes) {
      expect(stop[code], `${app}/${culture} has no words for "${code}"`).toBeTruthy();
    }
  });
});
