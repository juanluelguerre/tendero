import type { ActivatedRouteSnapshot } from '@angular/router';
import type { ScreenContext } from '@tendero/shared-api';

/** The query parameters that name the one thing a screen shows: its open row. */
const Subjects = ['product', 'order', 'return', 'claim', 'proposal', 'agent'];

/** The ones that say how a list is narrowed, when no row is open. */
const Filters = ['status', 'filter', 'support'];

/** As the server allows it (`ScreenContext.MaxLength`): a name, a code, a number. */
const MaxLength = 100;

/**
 * The screen a question is asked on (spec, *The panel*): its name in the
 * person's language, and what it shows — the open row, or else the filter —
 * so "translate this one" needs no code. The name is the route's title, or,
 * for a screen without one (the dashboard), the rail's word for its first
 * segment. It is data the turn sends; the model is told it is data.
 */
export function screenOf(root: ActivatedRouteSnapshot, name: (key: string) => string): ScreenContext | null {
  let leaf = root;
  while (leaf.firstChild) leaf = leaf.firstChild;

  const titled = [...leaf.pathFromRoot].reverse().find((route) => typeof route.title === 'string');
  const segment = root.firstChild?.url[0]?.path;
  const key = (titled?.title as string | undefined) ?? (segment ? `nav.${segment}` : null);
  if (!key) return null;

  const query = leaf.queryParamMap;
  const subject = [...Subjects, ...Filters].map((param) => query.get(param)).find((value) => !!value) ?? null;

  return { page: oneLine(name(key)), subject: subject && oneLine(subject) };
}

function oneLine(text: string): string {
  return text.replace(/\s+/g, ' ').trim().slice(0, MaxLength);
}
