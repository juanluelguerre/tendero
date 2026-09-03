/**
 * The same record without one key.
 *
 * A named function rather than `({ [key]: _, ...rest }) => rest`: the
 * destructuring idiom needs an unused binding, which is exactly the thing a
 * linter is right to complain about, and "without" says what it does.
 */
export function without<T>(record: Record<string, T>, key: string): Record<string, T> {
  const next = { ...record };
  delete next[key];
  return next;
}
