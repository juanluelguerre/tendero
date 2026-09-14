/** Who a row says acted: a label to print, or a guest the template names in the
    viewer's language. */
export type AuditActor = { label: string } | { guest: true };

/**
 * The actor of an audit row.
 *
 * The name first, because a real issuer signs an opaque subject a person cannot
 * read; the subject when a row predates names. And **no subject at all is a
 * guest**, not the system: the worker's own principal signs as "system", so a
 * row that nobody signed came through the storefront from somebody who never
 * signed in. Printing "system" there read a guest's checkout as something the
 * shop had done to itself (UI review, 2026-09-11).
 */
export function actorOf(entry: { actorName?: string | null; subject?: string | null }): AuditActor {
  const label = entry.actorName ?? entry.subject;

  return label ? { label } : { guest: true };
}
