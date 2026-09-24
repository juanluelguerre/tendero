const key = 'tendero.telemetry.session';

/**
 * The anonymous session id every record carries: random, per browser tab, gone
 * when the tab closes.
 *
 * Tab-scoped on purpose. It joins a visit's pages into one session and nothing
 * else — it is not a visitor id, it is never joined to an account or an order,
 * and it does not follow anyone across sites, which is what keeps first-party
 * audience measurement inside the exemption the AEPD and the CNIL describe
 * (`docs/analysis/observability-tooling.md` §7). Storage that is unavailable
 * (a private window, a blocked origin) is a new id per page, never an error.
 */
export function sessionId(storage: Pick<Storage, 'getItem' | 'setItem'> | null = safeSessionStorage()): string {
  try {
    const existing = storage?.getItem(key);
    if (existing) return existing;

    const created = newId();
    storage?.setItem(key, created);
    return created;
  } catch {
    return newId();
  }
}

function newId(): string {
  return crypto.randomUUID().replace(/-/g, '');
}

function safeSessionStorage(): Storage | null {
  try {
    return globalThis.sessionStorage ?? null;
  } catch {
    return null;
  }
}
