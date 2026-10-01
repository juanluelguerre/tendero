/** What `valueOf` needs of a resource: Angular's `Resource`, `HttpResourceRef`, `rxResource`. */
export interface ReadableResource<T> {
  hasValue(): boolean;
  value(): T;
}

/**
 * A resource's value, or undefined while it has none — still loading, or failed.
 *
 * In Angular 22 reading `value()` of a resource in its error state throws, and
 * so does every `computed` that reads it: a list whose request answered 500 sat
 * on "Loading" for good, its failure never reaching the template (found on
 * 2026-10-01). Read through this, and a failure is just no value — the
 * resource's `status()` says why.
 */
export function valueOf<T>(resource: ReadableResource<T>): T | undefined {
  return resource.hasValue() ? resource.value() : undefined;
}
